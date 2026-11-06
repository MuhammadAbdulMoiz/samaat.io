// SPDX-License-Identifier: GPL-3.0-or-later
// Written for Samaat from docs/protocols/sony.md (clean-room: behaviour learned from Gadgetbridge, no code copied).

using Samaat.Core.Battery;
using Samaat.Core.NoiseControl;

namespace Samaat.Protocols.Sony;

/// <summary>Command payloads (first byte says what it is) and their replies.</summary>
internal static class SonyCommands
{
    public static readonly byte[] Init = [0x00, 0x00];
    public static readonly byte[] NoiseQuery = [0x66, 0x02];

    public const byte InitReply = 0x01;
    public const byte BatteryReply = 0x11;
    public const byte BatteryPush = 0x13;
    public const byte NoiseReply = 0x67;
    public const byte NoisePush = 0x69;

    /// <summary>The init reply of protocol v1 is 4 bytes; v2 sends 8.</summary>
    public const int InitReplyLengthV1 = 4;

    public static byte[] BatteryQuery(byte type) => [0x10, type];
}

/// <summary>A battery message: <c>11|13 t</c> then level and charging, for both buds when <c>t</c> is 1.</summary>
internal sealed record SonyBatteryReport(byte Type, int Level, ChargingState Charging, int? RightLevel, ChargingState RightCharging)
{
    public static SonyBatteryReport? TryParse(byte[] payload)
    {
        if (payload is not [SonyCommands.BatteryReply or SonyCommands.BatteryPush, var type, ..] || type > 2 || payload.Length < (type == 1 ? 6 : 4))
        {
            return null;
        }

        return type == 1
            ? new(type, payload[2], ChargingOf(payload[3]), payload[4], ChargingOf(payload[5]))
            : new(type, payload[2], ChargingOf(payload[3]), null, ChargingState.Unknown);
    }

    private static ChargingState ChargingOf(byte raw) => raw == 1 ? ChargingState.Charging : ChargingState.NotCharging;
}

/// <summary>A noise message (<c>67|69 02</c>, 8 bytes): the mode we show, plus the focus-on-voice and ambient level bytes.</summary>
internal sealed record SonyNoiseReport(NoiseMode Mode, byte Voice, byte AmbientLevel)
{
    public static SonyNoiseReport? TryParse(byte[] payload)
    {
        if (payload is not [SonyCommands.NoiseReply or SonyCommands.NoisePush, 0x02, var on, var wind, var sub, _, var voice, var level])
        {
            return null;
        }

        NoiseMode? mode = (on, wind, sub) switch
        {
            (0, _, _) => NoiseMode.Off,
            (1, 0, 0) or (1, 2, 0) => NoiseMode.Transparency,
            (1, 0, 1) or (1, 2, 1) or (1, 2, 2) => NoiseMode.NoiseCancelling,
            _ => null,
        };
        return mode is { } m ? new(m, voice, level) : null;
    }
}
