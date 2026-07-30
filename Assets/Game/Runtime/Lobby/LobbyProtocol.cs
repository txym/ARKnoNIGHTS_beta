using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using ArknoNights.Match;

namespace ArknoNights.Lobby
{
    public enum LobbyMessageKind
    {
        JoinRequest,
        JoinAccepted,
        RoomSnapshot,
        SetReady,
        Ping,
        Pong,
        Start,
        Leave,
        Reject
    }

    public enum LobbyProtocolError
    {
        None,
        EmptyFrame,
        FrameTooLarge,
        InvalidFrameLength,
        InvalidJson,
        UnsupportedProtocolVersion,
        UnknownMessageKind,
        MissingRequiredField,
        InvalidRoomCode,
        InvalidProfile,
        InvalidCompatibilityManifest,
        SnapshotTooLarge
    }

    [DataContract]
    public sealed class LobbyWireMessage
    {
        [DataMember(Name = "protocolVersion")]
        public int protocolVersion;
        [DataMember(Name = "kind")]
        public string kind;
        [DataMember(Name = "roomCode")]
        public string roomCode;
        [DataMember(Name = "playerId")]
        public string playerId;
        [DataMember(Name = "displayName")]
        public string displayName;
        [DataMember(Name = "avatarIndex")]
        public int avatarIndex;
        [DataMember(Name = "isReady")]
        public bool isReady;
        [DataMember(Name = "sentUnixMilliseconds")]
        public long sentUnixMilliseconds;
        [DataMember(Name = "snapshotJson")]
        public string snapshotJson;
        [DataMember(Name = "rejectionCode")]
        public string rejectionCode;
        [DataMember(Name = "matchProtocolVersion")]
        public string matchProtocolVersion;
        [DataMember(Name = "matchRulesVersion")]
        public string matchRulesVersion;
        [DataMember(Name = "battleCoreVersion")]
        public string battleCoreVersion;
        [DataMember(Name = "unitCatalogSha256")]
        public string unitCatalogSha256;
        [DataMember(Name = "abilityCatalogSha256")]
        public string abilityCatalogSha256;

        public MatchCompatibilityManifest CompatibilityManifest =>
            new MatchCompatibilityManifest(
                matchProtocolVersion,
                matchRulesVersion,
                battleCoreVersion,
                unitCatalogSha256,
                abilityCatalogSha256);
    }

    public static class LobbyProtocol
    {
        public const int ProtocolVersion = 1;
        public const int MaximumMessageBytes = 4096;
        public const int MaximumSnapshotJsonBytes = 2048;

        private const int LengthPrefixBytes = sizeof(int);
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        public static byte[] Encode(LobbyWireMessage message)
        {
            if (!TryValidate(message, out var error))
            {
                throw new ArgumentException("The lobby message is invalid: " + error + ".", nameof(message));
            }

            var payload = StrictUtf8.GetBytes(LobbyJson.Serialize(message));
            if (payload.Length + LengthPrefixBytes > MaximumMessageBytes)
            {
                throw new ArgumentException("The lobby message exceeds the maximum frame size.", nameof(message));
            }

            var frame = new byte[payload.Length + LengthPrefixBytes];
            WriteNetworkOrderLength(frame, payload.Length);
            Buffer.BlockCopy(payload, 0, frame, LengthPrefixBytes, payload.Length);
            return frame;
        }

        public static bool TryDecode(byte[] frame, out LobbyWireMessage message, out LobbyProtocolError error)
        {
            message = null;
            if (frame == null || frame.Length == 0)
            {
                error = LobbyProtocolError.EmptyFrame;
                return false;
            }

            if (frame.Length > MaximumMessageBytes)
            {
                error = LobbyProtocolError.FrameTooLarge;
                return false;
            }

            if (frame.Length <= LengthPrefixBytes)
            {
                error = LobbyProtocolError.InvalidFrameLength;
                return false;
            }

            var payloadLength = ReadNetworkOrderLength(frame);
            if (payloadLength < 1 || payloadLength != frame.Length - LengthPrefixBytes)
            {
                error = LobbyProtocolError.InvalidFrameLength;
                return false;
            }

            string json;
            try
            {
                json = StrictUtf8.GetString(frame, LengthPrefixBytes, payloadLength);
                message = LobbyJson.Deserialize<LobbyWireMessage>(json);
            }
            catch (Exception)
            {
                error = LobbyProtocolError.InvalidJson;
                return false;
            }

            if (!TryValidate(message, out error))
            {
                message = null;
                return false;
            }

            return true;
        }

        private static bool TryValidate(LobbyWireMessage message, out LobbyProtocolError error)
        {
            if (message == null)
            {
                error = LobbyProtocolError.InvalidJson;
                return false;
            }

            if (message.protocolVersion != ProtocolVersion)
            {
                error = LobbyProtocolError.UnsupportedProtocolVersion;
                return false;
            }

            if (!Enum.TryParse(message.kind, false, out LobbyMessageKind kind)
                || !Enum.IsDefined(typeof(LobbyMessageKind), kind))
            {
                error = LobbyProtocolError.UnknownMessageKind;
                return false;
            }

            if (!LobbyRoomCode.IsValid(message.roomCode))
            {
                error = LobbyProtocolError.InvalidRoomCode;
                return false;
            }

            if (string.IsNullOrWhiteSpace(message.playerId))
            {
                error = LobbyProtocolError.MissingRequiredField;
                return false;
            }

            if (kind == LobbyMessageKind.JoinRequest)
            {
                var profile = new LobbyProfile(message.playerId, message.displayName, message.avatarIndex);
                if (!profile.IsValid())
                {
                    error = LobbyProtocolError.InvalidProfile;
                    return false;
                }
                if (!message.CompatibilityManifest.IsValid)
                {
                    error = LobbyProtocolError.InvalidCompatibilityManifest;
                    return false;
                }
            }

            if (kind == LobbyMessageKind.RoomSnapshot || kind == LobbyMessageKind.JoinAccepted)
            {
                if (string.IsNullOrWhiteSpace(message.snapshotJson))
                {
                    error = LobbyProtocolError.MissingRequiredField;
                    return false;
                }

                if (StrictUtf8.GetByteCount(message.snapshotJson) > MaximumSnapshotJsonBytes)
                {
                    error = LobbyProtocolError.SnapshotTooLarge;
                    return false;
                }
            }

            if (kind == LobbyMessageKind.Reject && string.IsNullOrWhiteSpace(message.rejectionCode))
            {
                error = LobbyProtocolError.MissingRequiredField;
                return false;
            }

            error = LobbyProtocolError.None;
            return true;
        }

        private static void WriteNetworkOrderLength(byte[] frame, int payloadLength)
        {
            frame[0] = (byte)(payloadLength >> 24);
            frame[1] = (byte)(payloadLength >> 16);
            frame[2] = (byte)(payloadLength >> 8);
            frame[3] = (byte)payloadLength;
        }

        private static int ReadNetworkOrderLength(byte[] frame)
        {
            return (frame[0] << 24)
                | (frame[1] << 16)
                | (frame[2] << 8)
                | frame[3];
        }

    }

    internal static class LobbyJson
    {
        public static string Serialize<T>(T value)
        {
            var serializer = new DataContractJsonSerializer(typeof(T));
            using (var stream = new MemoryStream())
            {
                serializer.WriteObject(stream, value);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        public static T Deserialize<T>(string json)
        {
            var serializer = new DataContractJsonSerializer(typeof(T));
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                return (T)serializer.ReadObject(stream);
            }
        }
    }
}
