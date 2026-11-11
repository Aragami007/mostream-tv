import json
import os
import subprocess
import threading
import time

from twisted.internet import reactor

from syncplay import constants
from syncplay.players.basePlayer import BasePlayer


class WmpPlayer(BasePlayer):
    speedSupported = True
    alertOSDSupported = True
    chatOSDSupported = True
    customOpenDialog = False

    def __init__(self, client, playerPath, filePath, args):
        self._client = client
        self._playerPath = playerPath
        self._filepath = filePath or None
        self._filename = os.path.basename(filePath) if filePath else None
        self._paused = True
        self._position = 0.0
        self._duration = 0.0
        self._dropped = False
        self._write_lock = threading.Lock()
        self._seek_guard_until = 0.0
        self._pause_guard_until = 0.0

        host_exe = self._resolve_host_executable()
        cmd = [host_exe, "--wmp-player"]
        if self._filepath:
            cmd.extend(["--file", self._filepath])

        creation_flags = getattr(subprocess, "CREATE_NO_WINDOW", 0)
        self._proc = subprocess.Popen(
            cmd,
            stdin=subprocess.PIPE,
            stdout=subprocess.PIPE,
            stderr=subprocess.DEVNULL,
            text=True,
            encoding="utf-8",
            errors="replace",
            bufsize=1,
            creationflags=creation_flags,
        )

        self._reader_thread = threading.Thread(
            target=self._read_stdout_loop,
            name="MostreamWmpReader",
            daemon=True,
        )
        self._reader_thread.start()

    @staticmethod
    def _resolve_host_executable():
        env_exe = os.environ.get("MOSTREAM_WMP_HOST_EXE")
        if env_exe and os.path.isfile(env_exe):
            return env_exe
        raise RuntimeError("Missing MOSTREAM_WMP_HOST_EXE for Windows Media Player playback.")

    @staticmethod
    def getDefaultPlayerPathsList():
        program_files = os.environ.get("ProgramFiles", r"C:\Program Files")
        program_files_x86 = os.environ.get("ProgramFiles(x86)", r"C:\Program Files (x86)")
        return [
            os.path.join(program_files, "Windows Media Player", "wmplayer.exe"),
            os.path.join(program_files_x86, "Windows Media Player", "wmplayer.exe"),
        ]

    @staticmethod
    def isValidPlayerPath(path):
        if not path:
            return False
        return os.path.basename(str(path).strip().strip('"')).lower() == "wmplayer.exe" and os.path.isfile(str(path).strip().strip('"'))

    @staticmethod
    def getIconPath(path):
        return "mplayer.png"

    @staticmethod
    def getExpandedPath(playerPath):
        if not playerPath:
            return playerPath
        cleaned = str(playerPath).strip().strip('"')
        return os.path.abspath(cleaned) if os.path.isfile(cleaned) else cleaned

    @staticmethod
    def getPlayerPathErrors(playerPath, filePath):
        return ""

    @classmethod
    def run(cls, client, playerPath, filePath, args):
        return cls(client, playerPath, filePath, args)

    def _send_command(self, payload):
        if self._dropped or not self._proc or self._proc.poll() is not None:
            return
        stdin = self._proc.stdin
        if stdin is None:
            return
        line = json.dumps(payload, ensure_ascii=False) + "\n"
        with self._write_lock:
            try:
                stdin.write(line)
                stdin.flush()
            except Exception:
                pass

    def _read_stdout_loop(self):
        stdout = self._proc.stdout if self._proc else None
        if stdout is None:
            return
        try:
            for raw_line in stdout:
                line = raw_line.strip()
                if not line:
                    continue
                try:
                    msg = json.loads(line)
                except Exception:
                    continue
                if isinstance(msg, dict):
                    self._handle_player_event(msg)
        except Exception:
            pass
        finally:
            if not self._dropped:
                reactor.callFromThread(self._handle_closed)

    def _handle_player_event(self, msg):
        event_type = msg.get("type")
        if event_type == "ready":
            reactor.callFromThread(self._client.initPlayer, self)
        elif event_type == "file":
            filepath = msg.get("filepath") or self._filepath
            filename = msg.get("filename") or (os.path.basename(filepath) if filepath else None)
            duration = float(msg.get("duration") or 0.0)
            self._filepath = filepath
            self._filename = filename
            self._duration = duration
            reactor.callFromThread(self._notify_file_loaded, filename, duration, filepath)
        elif event_type == "status":
            now = time.monotonic()
            if "paused" in msg and now >= self._pause_guard_until:
                self._paused = bool(msg.get("paused"))
            if "position" in msg and now >= self._seek_guard_until:
                try:
                    self._position = max(0.0, float(msg.get("position") or 0.0))
                except (TypeError, ValueError):
                    pass
            if "duration" in msg:
                try:
                    self._duration = max(0.0, float(msg.get("duration") or 0.0))
                except (TypeError, ValueError):
                    pass
        elif event_type == "error":
            err = str(msg.get("message") or "").strip()
            if err and not self._dropped:
                reactor.callFromThread(self._client.ui.showErrorMessage, err)
        elif event_type == "closing":
            if not self._dropped:
                reactor.callFromThread(self._handle_closed)

    def _notify_file_loaded(self, filename, duration, filepath):
        if self._dropped or not filename:
            return
        self._client.updateFile(filename, duration, filepath)

    def _handle_closed(self):
        if self._dropped:
            return
        self._dropped = True
        try:
            self._client.stop(False)
        except Exception:
            pass

    def askForStatus(self):
        if self._dropped:
            return
        self._send_command({"cmd": "ask_status"})
        self._client.updatePlayerStatus(self._paused, self._position)

    def setPaused(self, value):
        paused = bool(value)
        self._paused = paused
        self._pause_guard_until = time.monotonic() + 0.35
        self._send_command({"cmd": "set_paused", "paused": paused})

    def setPosition(self, value):
        try:
            pos = max(0.0, float(value))
        except (TypeError, ValueError):
            return
        self._position = pos
        self._seek_guard_until = time.monotonic() + 0.35
        self._send_command({"cmd": "set_position", "position": pos})

    def setSpeed(self, value):
        try:
            speed = float(value)
        except (TypeError, ValueError):
            return
        self._send_command({"cmd": "set_speed", "speed": speed})

    def openFile(self, filePath, resetPosition=False):
        if not filePath:
            return
        self._filepath = filePath
        self._filename = os.path.basename(filePath)
        if resetPosition:
            self._position = 0.0
            self._seek_guard_until = time.monotonic() + 0.35
        self._send_command({
            "cmd": "open_file",
            "path": filePath,
            "resetPosition": bool(resetPosition),
        })

    def displayMessage(
        self,
        message,
        duration=constants.OSD_DURATION,
        OSDType=constants.OSD_NOTIFICATION,
        mood=constants.MESSAGE_NEUTRAL,
    ):
        if message:
            self._send_command({
                "cmd": "osd",
                "text": str(message),
                "durationMs": int((duration or 3) * 1000),
            })

    def displayChatMessage(self, username, message):
        if message:
            self._send_command({
                "cmd": "osd",
                "text": f"<{username}> {message}",
                "durationMs": 4000,
            })

    def setFeatures(self, featureList):
        pass

    def drop(self):
        if self._dropped:
            return
        self._dropped = True
        stdin = self._proc.stdin if self._proc else None
        if stdin is not None:
            with self._write_lock:
                try:
                    stdin.write(json.dumps({"cmd": "quit"}) + "\n")
                    stdin.flush()
                    stdin.close()
                except Exception:
                    pass
        if self._proc and self._proc.poll() is None:
            try:
                self._proc.wait(timeout=1.5)
            except Exception:
                try:
                    self._proc.terminate()
                except Exception:
                    pass
