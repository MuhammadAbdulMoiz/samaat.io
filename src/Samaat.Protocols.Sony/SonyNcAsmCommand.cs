// SPDX-License-Identifier: GPL-3.0-or-later
// Ported from https://github.com/Plutoberth/SonyHeadphonesClient @ 5620e8ed5deccb957338b54e371b215146080819, Client/CommandSerializer.cpp, Client/Constants.h, Copyright (c) 2020 Nir Harel, Mor Gal, Sem Visscher, jimzrt, guilhermealbm, and other contributors, MIT.
// Changes: rewritten in C# onto Samaat.Transport (IRfcommChannel).

namespace Samaat.Protocols.Sony;

/// <summary>The noise cancelling and ambient sound set command, as SonyHeadphonesClient serializes it.</summary>
internal static class SonyNcAsmCommand
{
    public const byte CommandType = 0x68;
    public const byte LevelOff = 0xFF;

    private const byte NcAsmMode = 0x02;
    private const byte LevelAdjustment = 0x01;
    private const int MaxLevel = 19;

    /// <summary><c>68 02 effect 01 dualSingle 01 voice level</c>: effect 00 off or 11 on, level 0-19 (FF when off).</summary>
    public static byte[] Build(byte effect, byte voice, byte level)
    {
        // SHC reads the level as a signed char, so FF is -1: it passes the max check and maps to "off".
        if (level != LevelOff && level > MaxLevel)
        {
            throw new ArgumentOutOfRangeException(nameof(level), level, "Exceeded max steps");
        }

        byte dualSingle = level switch { 1 => 1, 0 => 2, _ => 0 };
        return [CommandType, NcAsmMode, effect, LevelAdjustment, dualSingle, LevelAdjustment, voice, level];
    }
}
