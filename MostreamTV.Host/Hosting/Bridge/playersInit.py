import os

from syncplay.players.basePlayer import DummyPlayer
from syncplay.players.mpc import MPCHCAPIPlayer
from syncplay.players.mpcbe import MpcBePlayer
from syncplay.players.mplayer import MplayerPlayer
from syncplay.players.mpv import MpvPlayer
from syncplay.players.mpvnet import MpvnetPlayer
from syncplay.players.memento import MementoPlayer
from syncplay.players.vlc import VlcPlayer
from syncplay.players.wmp import WmpPlayer

try:
    from syncplay.players.iina import IinaPlayer
except ImportError:
    IinaPlayer = None

_orig_mplayer_is_valid = MplayerPlayer.isValidPlayerPath


@staticmethod
def _guarded_mplayer_is_valid(path):
    if path and os.path.basename(str(path).strip().strip('"')).lower() == "wmplayer.exe":
        return False
    return _orig_mplayer_is_valid(path)


MplayerPlayer.isValidPlayerPath = _guarded_mplayer_is_valid


def getAvailablePlayers():
    players = [
        WmpPlayer,
        VlcPlayer,
        MpvPlayer,
        MpvnetPlayer,
        MPCHCAPIPlayer,
        MpcBePlayer,
        MementoPlayer,
        MplayerPlayer,
    ]
    if IinaPlayer is not None:
        players.append(IinaPlayer)
    return players
