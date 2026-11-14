//
// Copyright (c) 2026 Muhammad Waleed Badar
// SPDX-License-Identifier: Apache-2.0
//
using System;
using Antmicro.Renode.Core;
using Antmicro.Renode.Core.Structure.Registers;

namespace Antmicro.Renode.Peripherals.Miscellaneous
{
    // PADS_BANK0: pad control of the 48 user pads. Only the input enable and
    // the isolation bit are acted on; the other fields are stored.
    public class RP2350_Pads : RP2350_RegisterBlock
    {
        public RP2350_Pads(IMachine machine) : base(machine)
        {
            Registers.VoltageSelect.Define(this)
                .WithFlag(0, name: "VOLTAGE_SELECT")
                .WithReservedBits(1, 31);

            Registers.Gpio0.DefineMany(this, NumberOfPads, (register, index) =>
            {
                register
                    .WithValueField(0, 6, name: "SLEWFAST_SCHMITT_PDE_PUE_DRIVE")
                    .WithFlag(6, out inputEnable[index], name: "IE")
                    .WithFlag(7, name: "OD")
                    .WithFlag(8, out isolated[index], name: "ISO")
                    .WithReservedBits(9, 23)
                    .WithChangeCallback((_, __) => PadChanged?.Invoke(index));
            }, resetValue: PadReset);

            Registers.Swclk.Define(this, DebugPadReset)
                .WithValueField(0, 9, name: "SWCLK")
                .WithReservedBits(9, 23);

            Registers.Swd.Define(this, DebugPadReset)
                .WithValueField(0, 9, name: "SWD")
                .WithReservedBits(9, 23);
        }

        public bool IsInputEnabled(int pad)
        {
            return inputEnable[pad].Value;
        }

        public bool IsIsolated(int pad)
        {
            return isolated[pad].Value;
        }

        public event Action<int> PadChanged;

        public const int NumberOfPads = 48;

        private readonly IFlagRegisterField[] inputEnable = new IFlagRegisterField[NumberOfPads];
        private readonly IFlagRegisterField[] isolated = new IFlagRegisterField[NumberOfPads];

        // Isolated, input buffer off, 4 mA, pull-down, Schmitt trigger.
        private const uint PadReset = 0x116;
        private const uint DebugPadReset = 0x5A;

        private enum Registers : long
        {
            VoltageSelect = 0x00,
            Gpio0 = 0x04,
            Swclk = 0xC4,
            Swd = 0xC8,
        }
    }
}
