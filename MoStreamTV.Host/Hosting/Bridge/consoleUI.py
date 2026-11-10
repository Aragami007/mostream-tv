import json
import os
import re
import sys
import threading
import time

from twisted.internet import reactor, task

import syncplay
from syncplay import constants
from syncplay import messages as syncplay_messages
from syncplay import utils
from syncplay.messages import getMessage
from syncplay.utils import formatSize, formatTime, isURL


BRIDGE_PREFIX = "@@MOSTREAM@@"
_HTML_TAG_RE = re.compile(r"<[^>]+>")


def _is_bridge_mode():
    return os.environ.get("MOSTREAM_BRIDGE") == "1"


def _sanitize_text(text):
    if text is None:
        return ""
    cleaned = _HTML_TAG_RE.sub("", str(text))
    cleaned = cleaned.replace("&amp;", "&").replace("&lt;", "<").replace("&gt;", ">").replace("&quot;", '"')
    cleaned = re.sub(r"\bSyncplay\b", "MoStream TV", cleaned)
    return cleaned


def _rebrand_messages():
    overrides = {
        "vlc-initial-warning": (
            "Note: VLC may occasionally report imprecise seeking on some .mp4 and .avi files. "
            "If you notice seek drift, mpv or mpv.net are recommended."
        ),
        "vlc-failed-connection": (
            "Failed to connect to VLC. Please make sure VLC 3.x is installed "
            "(VLC 4 is not supported), or choose another supported player such as mpv."
        ),
        "vlc-failed-noscript": (
            "VLC could not load the playback synchronization interface. "
            "Try restarting the watch party or selecting mpv."
        ),
        "media-directory-list-updated-notification": "MoStream TV media folders have been updated.",
        "switch-file-not-found-error": "Could not find '{}' locally. Select your local copy in MoStream TV.",
        "cannot-find-file-for-playlist-switch-error": (
            "Could not find '{}' locally to switch playback. "
            "Choose your local copy in MoStream TV."
        ),
        "mpv-unresponsive-error": "mpv has not responded for {} seconds. Please restart the watch party.",
        "alphakey-mode-warning-second-line": "Press [TAB] to return to MoStream TV chat mode.",
        "startTLS-server-certificate-invalid": "Secure connection failed because the server certificate could not be verified.",
        "startTLS-server-certificate-invalid-DNS-ID": "The server certificate is not valid for this hostname.",
        "no-media-directories-error": "No media folders are configured.",
    }
    for lang, table in list(syncplay_messages.messages.items()):
        if not isinstance(table, dict):
            continue
        for key, value in list(table.items()):
            if key in overrides:
                table[key] = overrides[key]
            elif isinstance(value, str) and "Syncplay" in value and "http" not in value:
                table[key] = value.replace("Syncplay", "MoStream TV")


class _LegacyConsoleUI(threading.Thread):
    def __init__(self):
        self.promptMode = threading.Event()
        self.PromptResult = ""
        self.promptMode.set()
        self._syncplayClient = None
        self.uiMode = constants.CONSOLE_UI_MODE
        threading.Thread.__init__(self, name="ConsoleUI")

    def addClient(self, client):
        self._syncplayClient = client

    def addFileToPlaylist(self, file):
        self._syncplayClient.playlist.addToPlaylist(file)

    def drop(self):
        pass

    def setPlaylist(self, newPlaylist, newIndexFilename=None):
        pass

    def setPlaylistIndexFilename(self, filename):
        pass

    def run(self):
        try:
            while True:
                data = input()
                data = data.rstrip('\n\r')
                if not self.promptMode.isSet():
                    self.PromptResult = data
                    self.promptMode.set()
                elif self._syncplayClient:
                    self.executeCommand(data)
        except EOFError:
            pass

    def updateRoomName(self, room=""):
        pass

    def updateAutoPlayState(self, newState):
        pass

    def promptFor(self, prompt=">", message=""):
        if message != "":
            print(message)
        self.promptMode.clear()
        print(prompt, end='')
        self.promptMode.wait()
        return self.PromptResult

    def showUserList(self, currentUser, rooms):
        for room in rooms:
            message = "In room '{}':".format(room)
            self.showMessage(message, True)
            for user in rooms[room]:
                userflags = ""
                if user.isController():
                    userflags += "({}) ".format(getMessage("controller-userlist-userflag"))
                if user.isReady():
                    userflags += "({}) ".format(getMessage("ready-userlist-userflag"))

                username = userflags + "*<{}>*".format(user.username) if user == currentUser else userflags + "<{}>".format(user.username)
                if user.file:
                    message = getMessage("userlist-playing-notification").format(username)
                    self.showMessage(message, True)
                    message = "    {}: '{}' ({})".format(getMessage("userlist-file-notification"), user.file['name'], formatTime(user.file['duration']))
                    if currentUser.file:
                        if user.file['name'] == currentUser.file['name'] and user.file['size'] != currentUser.file['size']:
                            message += getMessage("different-filesize-notification")
                    self.showMessage(message, True)
                else:
                    message = getMessage("no-file-played-notification").format(username)
                    self.showMessage(message, True)

    def userListChange(self):
        pass

    def addRoomToList(self, room=None):
        pass

    def fileSwitchFoundFiles(self):
        pass

    def setFeatures(self, featureList):
        pass

    def setControllerStatus(self, username, isController):
        pass

    def markEndOfUserlist(self):
        pass

    def showMessage(self, message, noTimestamp=False, isMotd=False):
        message = message.encode(sys.stdout.encoding, 'replace')
        try:
            message = message.decode('utf-8')
        except UnicodeEncodeError:
            pass
        if noTimestamp:
            print(message)
        else:
            print(time.strftime(constants.UI_TIME_FORMAT, time.localtime()) + message)

    def showDebugMessage(self, message):
        print(message)

    def showErrorMessage(self, message, criticalerror=False):
        print("ERROR:\t" + message)

    def setSSLMode(self, sslMode, sslInformation=""):
        pass

    def _extractSign(self, m):
        if m:
            if m == "-":
                return -1
            else:
                return 1
        else:
            return None

    def _tryAdvancedCommands(self, data):
        o = re.match(constants.UI_OFFSET_REGEX, data)
        s = re.match(constants.UI_SEEK_REGEX, data)
        if o:
            sign = self._extractSign(o.group('sign'))
            t = utils.parseTime(o.group('time'))
            if t is None:
                return
            if o.group('sign') == "/":
                t = self._syncplayClient.getPlayerPosition() - t
            elif sign:
                t = self._syncplayClient.getUserOffset() + sign * t
            self._syncplayClient.setUserOffset(t)
            return True
        elif s:
            sign = self._extractSign(s.group('sign'))
            t = utils.parseTime(s.group('time'))
            if t is None:
                return
            if sign:
                t = self._syncplayClient.getGlobalPosition() + sign * t
            self._syncplayClient.setPosition(t)
            return True
        return False

    def executeCommand(self, data):
        command = re.match(constants.UI_COMMAND_REGEX, data)
        if not command:
            return
        if command.group('command') in constants.COMMANDS_UNDO:
            tmp_pos = self._syncplayClient.getPlayerPosition()
            self._syncplayClient.setPosition(self._syncplayClient.playerPositionBeforeLastSeek)
            self._syncplayClient.playerPositionBeforeLastSeek = tmp_pos
        elif command.group('command') in constants.COMMANDS_LIST:
            self.getUserlist()
        elif command.group('command') in constants.COMMANDS_CHAT:
            message = command.group('parameter')
            self._syncplayClient.sendChat(message)
        elif command.group('command') in constants.COMMANDS_PAUSE:
            self._syncplayClient.setPaused(not self._syncplayClient.getPlayerPaused())
        elif command.group('command') in constants.COMMANDS_ROOM:
            room = command.group('parameter')
            if room is None:
                if self._syncplayClient.userlist.currentUser.file:
                    room = self._syncplayClient.userlist.currentUser.file["name"]
                else:
                    room = self._syncplayClient.defaultRoom
            self._syncplayClient.setRoom(room, resetAutoplay=True)
            self._syncplayClient.ui.updateRoomName(room)
            self._syncplayClient.sendRoom()
        elif command.group('command') in constants.COMMANDS_CREATE:
            roombasename = command.group('parameter')
            if roombasename is None:
                roombasename = self._syncplayClient.getRoom()
            roombasename = utils.stripRoomName(roombasename)
            self._syncplayClient.createControlledRoom(roombasename)
        elif command.group('command') in constants.COMMANDS_AUTH:
            controlpassword = command.group('parameter')
            self._syncplayClient.identifyAsController(controlpassword)
        elif command.group('command') in constants.COMMANDS_TOGGLE:
            self._syncplayClient.toggleReady()
        elif command.group('command') in constants.COMMANDS_QUEUE:
            filename = command.group('parameter')
            if filename is None:
                self.showErrorMessage("No file/url given")
                return
            self._syncplayClient.ui.addFileToPlaylist(filename)
        elif command.group('command') in constants.COMMANDS_QUEUEANDSELECT:
            self._syncplayClient.playlist.switchToNewPlaylistItem = True
            self.executeCommand("{} {}".format(constants.COMMANDS_QUEUE[0], command.group('parameter')))
        elif command.group('command') in constants.COMMANDS_PLAYLIST:
            playlist = self._syncplayClient.playlist
            playlist_elements = ["\t{}: {}".format(i + 1, el) for i, el in enumerate(playlist._playlist)]

            if playlist_elements:
                i = playlist._playlistIndex
                if i is not None and i in range(len(playlist_elements)):
                    playlist_elements[i] = " *" + playlist_elements[i]

                self.showMessage("\n".join(playlist_elements), True)
            else:
                self.showMessage(getMessage("playlist-empty-error"), True)
        elif command.group('command') in constants.COMMANDS_SELECT:
            try:
                index = int(command.group('parameter').strip()) - 1

                if index < 0 or index >= len(self._syncplayClient.playlist._playlist):
                    raise TypeError("Invalid playlist index")
                self._syncplayClient.playlist.changeToPlaylistIndex(index, resetPosition=True)
                self._syncplayClient.rewindFile()

            except (TypeError, AttributeError):
                self.showErrorMessage(getMessage("playlist-invalid-index-error"))
        elif command.group('command') in constants.COMMANDS_DELETE:
            try:
                index = int(command.group('parameter').strip()) - 1
                self._syncplayClient.playlist.deleteAtIndex(index)

            except (TypeError, AttributeError):
                self.showErrorMessage(getMessage("playlist-invalid-index-error"))
        elif command.group('command') in constants.COMMANDS_NEXT:
            self._syncplayClient.playlist.loadNextFileInPlaylist()

        elif command.group('command') in constants.COMMANDS_SETREADY:
            try:
                username = command.group('parameter')
                self._syncplayClient.setOthersReadiness(username, True)
            except Exception:
                pass

        elif command.group('command') in constants.COMMANDS_SETNOTREADY:
            try:
                username = command.group('parameter')
                self._syncplayClient.setOthersReadiness(username, False)
            except Exception:
                pass

        else:
            if self._tryAdvancedCommands(data):
                return
            if command.group('command') not in constants.COMMANDS_HELP:
                self.showMessage(getMessage("unrecognized-command-notification"))
            self.showMessage(getMessage("commandlist-notification"), True)

    def getUserlist(self):
        self._syncplayClient.getUserList()


class MoStreamBridgeUI(_LegacyConsoleUI):
    def __init__(self):
        super(MoStreamBridgeUI, self).__init__()
        if hasattr(sys.stdout, "reconfigure"):
            try:
                sys.stdout.reconfigure(encoding="utf-8", errors="replace")
            except Exception:
                pass
        if hasattr(sys.stdin, "reconfigure"):
            try:
                sys.stdin.reconfigure(encoding="utf-8", errors="replace")
            except Exception:
                pass

        _rebrand_messages()
        self.uiMode = constants.GRAPHICAL_UI_MODE
        self._emit_lock = threading.Lock()
        self._local_file_map = {}
        self._playlist = []
        self._playlist_index_filename = None
        self._player_ready = False
        self._poll_loop = None
        self._last_player_snapshot = None
        self._file_switch_wrapped = False
        self._shutting_down = False
        self._connected_once = False

    def _emit(self, payload):
        line = BRIDGE_PREFIX + json.dumps(payload, ensure_ascii=False)
        with self._emit_lock:
            try:
                sys.stdout.write(line + "\n")
                sys.stdout.flush()
            except Exception:
                pass

    def _register_local_path(self, path):
        if path and not isURL(path):
            normed = os.path.abspath(path)
            if os.path.isfile(normed):
                name = os.path.basename(normed)
                self._local_file_map[name.lower()] = normed
                return name
        return path

    def _resolve_local_path(self, filename):
        if not filename:
            return None
        mapped = self._local_file_map.get(filename.lower())
        if mapped and os.path.isfile(mapped):
            return mapped
        if self._syncplayClient and self._syncplayClient.userlist.currentUser.file:
            cur_path = self._syncplayClient.userlist.currentUser.file.get("path")
            if cur_path and not isURL(cur_path):
                candidate = os.path.join(os.path.dirname(cur_path), filename)
                if os.path.isfile(candidate):
                    self._local_file_map[filename.lower()] = candidate
                    return candidate
        return None

    def _wrap_file_switch(self):
        if self._file_switch_wrapped or not self._syncplayClient:
            return
        self._file_switch_wrapped = True
        fs = self._syncplayClient.fileSwitch
        orig_find = fs.findFilepath
        orig_notify = fs.notifyUserIfFileNotInMediaDirectory

        def wrapped_find(filename, highPriority=False):
            found = self._resolve_local_path(filename)
            if found:
                return found
            return orig_find(filename, highPriority)

        def wrapped_notify(filenameToFind, path):
            if filenameToFind and filenameToFind.lower() in self._local_file_map:
                return
            if path and os.path.isfile(path):
                self._local_file_map[os.path.basename(path).lower()] = os.path.abspath(path)
                return
            orig_notify(filenameToFind, path)

        fs.findFilepath = wrapped_find
        fs.notifyUserIfFileNotInMediaDirectory = wrapped_notify

    def addClient(self, client):
        super(MoStreamBridgeUI, self).addClient(client)
        self._wrap_file_switch()
        self._wrap_player_guards(client)
        if client._config.get("file"):
            self._register_local_path(client._config["file"])
        client.addPlayerReadyCallback(self._on_player_ready)

    def _wrap_player_guards(self, client):
        if getattr(client, "_mostream_player_guards", False):
            return
        client._mostream_player_guards = True

        targets = [client]
        ui = getattr(client, "ui", None)
        if ui is not None and ui is not client:
            targets.append(ui)

        for target in targets:
            if hasattr(target, "showOSDMessage"):
                orig_osd = target.showOSDMessage

                def guarded_osd(*args, __orig=orig_osd, **kwargs):
                    if getattr(client, "_player", None) is None:
                        return
                    return __orig(*args, **kwargs)

                target.showOSDMessage = guarded_osd

        chat_owner = ui if ui is not None and hasattr(ui, "showChatMessage") else client
        if hasattr(chat_owner, "showChatMessage"):
            orig_chat = chat_owner.showChatMessage

            def guarded_chat(username, message, *args, __orig=orig_chat, **kwargs):
                if getattr(client, "_player", None) is None:
                    self._emit({
                        "type": "chat",
                        "username": _sanitize_text("" if username is None else str(username)),
                        "message": _sanitize_text("" if message is None else str(message)).lstrip(),
                        "timestamp": time.strftime("%H:%M:%S", time.localtime()),
                    })
                    return
                return __orig(username, message, *args, **kwargs)

            chat_owner.showChatMessage = guarded_chat

    def _on_player_ready(self, player):
        self._player_ready = True
        self._emit({"type": "player_ready"})
        if self._poll_loop is None:
            self._poll_loop = task.LoopingCall(self._emit_player_state)
            self._poll_loop.start(0.5, now=True)

    def _emit_player_state(self):
        if not self._syncplayClient or not self._syncplayClient._running:
            return
        cur_file = self._syncplayClient.userlist.currentUser.file
        filename = cur_file.get("name") if cur_file else None
        filepath = cur_file.get("path") if cur_file else None
        duration = float(cur_file.get("duration") or 0.0) if cur_file else 0.0
        if self._syncplayClient._player:
            paused = self._syncplayClient.getPlayerPaused()
            position = self._syncplayClient.getPlayerPosition()
        else:
            paused = self._syncplayClient.getGlobalPaused()
            position = self._syncplayClient.getGlobalPosition()
        if position is None:
            position = 0.0
        snapshot = (bool(paused) if paused is not None else None, int(round(position)), int(round(duration)), filename, filepath)
        if snapshot != self._last_player_snapshot:
            self._last_player_snapshot = snapshot
            self._emit({
                "type": "player_state",
                "paused": snapshot[0],
                "position": float(position),
                "duration": duration,
                "filename": filename,
                "filepath": filepath,
            })

    def drop(self):
        if self._poll_loop and self._poll_loop.running:
            try:
                self._poll_loop.stop()
            except Exception:
                pass
        self._emit({"type": "dropped"})

    def promptFor(self, prompt=">", message=""):
        return ""

    def setFeatures(self, featureList):
        self._connected_once = True
        if self._syncplayClient:
            self._emit({
                "type": "connected",
                "username": self._syncplayClient.getUsername(),
                "room": self._syncplayClient.getRoom(),
                "features": featureList or {},
            })

    def updateRoomName(self, room=""):
        self._emit({"type": "room", "room": room or ""})

    def addRoomToList(self, room=None):
        if room:
            self._emit({"type": "room_added", "room": room})

    def updateAutoPlayState(self, newState):
        self._emit({"type": "autoplay", "enabled": bool(newState)})

    def setSSLMode(self, sslMode, sslInformation=""):
        self._emit({"type": "ssl", "enabled": bool(sslMode)})

    def setControllerStatus(self, username, isController):
        pass

    def markEndOfUserlist(self):
        pass

    def fileSwitchFoundFiles(self):
        if self._syncplayClient:
            self._syncplayClient.showUserList()

    def userListChange(self):
        if self._syncplayClient:
            self._syncplayClient.showUserList()

    def showUserList(self, currentUser, rooms):
        if self._syncplayClient and currentUser.file and currentUser.file.get("path"):
            path = currentUser.file["path"]
            if os.path.isfile(path):
                self._register_local_path(path)
                self._syncplayClient.fileSwitch.setCurrentDirectory(os.path.dirname(path))

        payload_rooms = []
        known_rooms = []
        for room_name, users in rooms.items():
            visible_users = [u for u in users if u.username and u.username.strip() != ""]
            known_rooms.append(room_name)
            user_rows = []
            for user in visible_users:
                same_room = room_name == currentUser.room
                file_info = user.file
                filename = file_info.get("name") if file_info else None
                duration = float(file_info.get("duration") or 0.0) if file_info else 0.0
                size = int(file_info.get("size") or 0) if file_info else 0
                differs = False
                diff_detail = ""
                if same_room and file_info and currentUser.file and user.username != currentUser.username:
                    diffs = self._syncplayClient.userlist.getFileDifferencesForUser(currentUser.file, file_info)
                    if diffs:
                        differs = True
                        diff_detail = diffs
                user_rows.append({
                    "username": user.username,
                    "isSelf": user.username == currentUser.username,
                    "isController": bool(user.isController()),
                    "isReady": user.isReadyWithFile() if same_room else None,
                    "filename": filename,
                    "duration": duration,
                    "durationText": formatTime(duration) if file_info else "",
                    "size": size,
                    "sizeText": formatSize(size) if file_info else "",
                    "differs": differs,
                    "diffDetail": diff_detail,
                })
            payload_rooms.append({
                "name": room_name,
                "isCurrent": room_name == currentUser.room,
                "users": user_rows,
            })

        self._emit({
            "type": "userlist",
            "currentUser": currentUser.username,
            "currentRoom": currentUser.room,
            "currentReady": currentUser.isReadyWithFile(),
            "currentFile": currentUser.file.get("name") if currentUser.file else None,
            "currentFilePath": currentUser.file.get("path") if currentUser.file else None,
            "knownRooms": known_rooms,
            "rooms": payload_rooms,
        })
        self._emit_player_state()

    def _emit_playlist(self):
        items = list(self._playlist)
        current_index = None
        if self._syncplayClient and self._syncplayClient.playlist:
            if self._syncplayClient.playlist._playlist:
                items = list(self._syncplayClient.playlist._playlist)
                self._playlist = items
            current_index = self._syncplayClient.playlist._playlistIndex
        if current_index is None and self._playlist_index_filename and self._playlist_index_filename in items:
            current_index = items.index(self._playlist_index_filename)
        enabled = self._syncplayClient.sharedPlaylistIsEnabled() if self._syncplayClient else True
        self._emit({
            "type": "playlist",
            "items": items,
            "currentIndex": current_index,
            "currentFilename": self._playlist_index_filename,
            "enabled": bool(enabled),
        })

    def setPlaylist(self, newPlaylist, newIndexFilename=None):
        self._playlist = list(newPlaylist or [])
        if newIndexFilename is not None:
            self._playlist_index_filename = newIndexFilename
        elif not self._playlist:
            self._playlist_index_filename = None
        self._emit_playlist()

    def setPlaylistIndexFilename(self, filename):
        self._playlist_index_filename = filename
        self._emit_playlist()

    def addFileToPlaylist(self, filePath):
        if not self._syncplayClient:
            return
        item = self._register_local_path(filePath)
        if item not in self._syncplayClient.playlist._playlist:
            self._syncplayClient.playlist.addToPlaylist(item)

    def showMessage(self, message, noTimestamp=False, isMotd=False):
        raw = "" if message is None else str(message)
        if not raw:
            return
        if self._shutting_down:
            return
        if not isMotd:
            chat_match = re.match(constants.MESSAGE_WITH_USERNAME_REGEX, raw, re.UNICODE)
            if chat_match:
                self._emit({
                    "type": "chat",
                    "username": _sanitize_text(chat_match.group("username")),
                    "message": _sanitize_text(chat_match.group("message")).lstrip(),
                    "timestamp": time.strftime("%H:%M:%S", time.localtime()),
                })
                return
        clean = _sanitize_text(raw)
        if not clean:
            return
        if clean in (
            getMessage("startTLS-initiated"),
            getMessage("startTLS-not-supported-server"),
        ):
            return
        self._emit({
            "type": "message",
            "kind": "motd" if isMotd else "info",
            "text": clean,
            "timestamp": "" if noTimestamp else time.strftime("%H:%M:%S", time.localtime()),
        })

    def showDebugMessage(self, message):
        if constants.DEBUG_MODE and message:
            self._emit({
                "type": "message",
                "kind": "debug",
                "text": _sanitize_text(message),
                "timestamp": time.strftime("%H:%M:%S", time.localtime()),
            })

    def showErrorMessage(self, message, criticalerror=False):
        if self._shutting_down:
            return
        clean = _sanitize_text(message).strip()
        if not clean:
            return
        if clean == getMessage("startTLS-not-supported-server"):
            return
        if clean.startswith("[Failure instance:") or clean.startswith("<class 'twisted."):
            return
        if clean == "Connection was closed cleanly.":
            return
        if clean == getMessage("vlc-initial-warning"):
            self._emit({
                "type": "message",
                "kind": "info",
                "text": clean,
                "timestamp": time.strftime("%H:%M:%S", time.localtime()),
            })
            return
        is_fatal_auth_or_connect = (not self._connected_once) and (
            clean in (
                getMessage("password-required-server-error"),
                getMessage("wrong-password-server-error"),
                getMessage("connection-failed-notification"),
                getMessage("version-mismatch-error").strip(),
            )
            or "password" in clean.lower()
        )
        self._emit({
            "type": "error",
            "text": clean,
            "critical": bool(criticalerror or is_fatal_auth_or_connect),
            "timestamp": time.strftime("%H:%M:%S", time.localtime()),
        })
        if is_fatal_auth_or_connect and self._syncplayClient:
            self._shutting_down = True
            reactor.callLater(0.05, self._syncplayClient.stop, False)

    def _open_file_when_ready(self, path, reset_position, add_to_playlist):
        client = self._syncplayClient
        if not client:
            return
        display_item = self._register_local_path(path)
        if client.playerIsNotReady():
            client.addPlayerReadyCallback(
                lambda _: self._open_file_when_ready(path, reset_position, add_to_playlist)
            )
            return
        client.openFile(path, resetPosition=reset_position, fromUser=True)
        if add_to_playlist and display_item and display_item not in client.playlist._playlist:
            client.playlist.addToPlaylist(display_item)

    def _handle_command(self, cmd_obj):
        client = self._syncplayClient
        if not client:
            return
        cmd = cmd_obj.get("cmd")
        if cmd == "open_file":
            path = (cmd_obj.get("path") or "").strip()
            if path:
                self._open_file_when_ready(
                    path,
                    bool(cmd_obj.get("resetPosition", False)),
                    bool(cmd_obj.get("addToPlaylist", False)),
                )
        elif cmd == "add_playlist_items":
            paths = cmd_obj.get("paths") or []
            open_if_idle = bool(cmd_obj.get("openIfIdle", False))
            was_empty = len(client.playlist._playlist) == 0
            new_list = list(client.playlist._playlist)
            first_raw = None
            for raw in paths:
                raw_str = (raw or "").strip()
                if not raw_str:
                    continue
                if first_raw is None:
                    first_raw = raw_str
                display_item = self._register_local_path(raw_str)
                if display_item not in new_list:
                    new_list.append(display_item)
                else:
                    self.showErrorMessage(getMessage("cannot-add-duplicate-error").format(display_item))
            if new_list != client.playlist._playlist:
                client.playlist.changePlaylist(new_list)
                if open_if_idle and not was_empty and not client.userlist.currentUser.file and first_raw:
                    self._open_file_when_ready(first_raw, False, False)
        elif cmd == "select_playlist_index":
            try:
                idx = int(cmd_obj.get("index"))
            except (TypeError, ValueError):
                return
            if 0 <= idx < len(client.playlist._playlist):
                filename = client.playlist._playlist[idx]
                if client.userlist.currentUser.file and filename == client.userlist.currentUser.file.get("name"):
                    return
                if isURL(filename):
                    self._open_file_when_ready(filename, True, False)
                else:
                    path_found = self._resolve_local_path(filename) or client.fileSwitch.findFilepath(filename, highPriority=True)
                    if path_found:
                        self._open_file_when_ready(path_found, True, False)
                    else:
                        self.showErrorMessage(getMessage("cannot-find-file-for-playlist-switch-error").format(filename))
        elif cmd == "remove_playlist_indices":
            indices = set()
            for val in cmd_obj.get("indices") or []:
                try:
                    indices.add(int(val))
                except (TypeError, ValueError):
                    pass
            if indices:
                new_list = [item for i, item in enumerate(client.playlist._playlist) if i not in indices]
                client.playlist.changePlaylist(new_list)
        elif cmd == "clear_playlist":
            client.playlist.changePlaylist([])
        elif cmd == "shuffle_playlist":
            client.playlist.shuffleEntirePlaylist()
        elif cmd == "set_paused":
            if client.playerIsNotReady():
                self.showErrorMessage("Media player is still loading.")
                return
            client.setPaused(bool(cmd_obj.get("paused")))
        elif cmd == "toggle_pause":
            if client.playerIsNotReady():
                self.showErrorMessage("Media player is still loading.")
                return
            client.setPaused(not client.getPlayerPaused())
        elif cmd == "seek":
            target = str(cmd_obj.get("target") or "").strip()
            if not target:
                return
            s = re.match(constants.UI_SEEK_REGEX, target)
            if not s:
                self.showErrorMessage(getMessage("invalid-seek-value"))
                return
            sign = self._extractSign(s.group("sign"))
            t = utils.parseTime(s.group("time"))
            if t is None:
                self.showErrorMessage(getMessage("invalid-seek-value"))
                return
            if sign:
                t = client.getGlobalPosition() + sign * t
            client.setPosition(max(0.0, t))
        elif cmd == "undo_seek":
            tmp_pos = client.getPlayerPosition()
            client.setPosition(client.playerPositionBeforeLastSeek)
            client.playerPositionBeforeLastSeek = tmp_pos
        elif cmd == "set_ready":
            client.changeReadyState(bool(cmd_obj.get("ready")), manuallyInitiated=True)
        elif cmd == "set_autoplay":
            if "minUsers" in cmd_obj and cmd_obj["minUsers"] is not None:
                try:
                    client.changeAutoPlayThrehsold(int(cmd_obj["minUsers"]))
                except (TypeError, ValueError):
                    pass
            if "enabled" in cmd_obj:
                client.changeAutoplayState(bool(cmd_obj["enabled"]))
        elif cmd == "set_room":
            room = str(cmd_obj.get("room") or "").strip()
            if not room:
                room = client.defaultRoom
            if room != client.getRoom():
                client.setRoom(room, resetAutoplay=True)
                client.ui.updateRoomName(room)
                client.sendRoom()
        elif cmd == "send_chat":
            msg = str(cmd_obj.get("message") or "").strip()
            if not msg:
                return
            if msg.startswith("/") and msg != "/":
                command = msg[1:]
                if command.startswith("/"):
                    client.sendChat(msg[1:])
                else:
                    self.executeCommand(command)
            else:
                client.sendChat(msg)
        elif cmd == "switch_to_file":
            filename = str(cmd_obj.get("filename") or "").strip()
            if not filename:
                return
            if isURL(filename):
                self._open_file_when_ready(filename, False, False)
                return
            path_found = self._resolve_local_path(filename) or client.fileSwitch.findFilepath(filename, highPriority=True)
            if path_found:
                self._open_file_when_ready(path_found, False, False)
            else:
                self.showErrorMessage(getMessage("switch-file-not-found-error").format(filename))
        elif cmd == "reconnect":
            client.manualReconnect()
        elif cmd == "stop":
            self._shutting_down = True
            client.stop(False)

    def run(self):
        try:
            while True:
                raw_line = sys.stdin.readline()
                if not raw_line:
                    break
                line = raw_line.strip()
                if not line:
                    continue
                try:
                    cmd_obj = json.loads(line)
                except Exception:
                    if self._syncplayClient:
                        reactor.callFromThread(self.executeCommand, line)
                    continue
                if isinstance(cmd_obj, dict):
                    reactor.callFromThread(self._handle_command, cmd_obj)
        except Exception:
            pass


if _is_bridge_mode():
    ConsoleUI = MoStreamBridgeUI
else:
    ConsoleUI = _LegacyConsoleUI
