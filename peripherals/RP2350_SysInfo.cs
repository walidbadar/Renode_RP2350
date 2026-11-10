//
// Copyright (c) 2026 Muhammad Waleed Badar
// SPDX-License-Identifier: Apache-2.0
//
using Antmicro.Renode.Core;
using Antmicro.Renode.Core.Structure.Registers;

namespace Antmicro.Renode.Peripherals.Miscellaneous
{
    // SYSINFO: what chip this is. Software reads it to tell an RP2350 from
    // an RP2040, the A package from the B package, and silicon revisions
    // apart.
    public class RP2350_SysInfo : RP2350_RegisterBlock
    {
        // revision: 2 is the A2 stepping, the first one sold.
        // qfn60: the RP2350A, with 30 GPIOs; otherwise the RP2350B, with 48.
        public RP2350_SysInfo(IMachine machine, uint revision = 2, bool qfn60 = true) : base(machine)
        {
            Registers.ChipId.Define(this)
                .WithFlag(0, FieldMode.Read, valueProviderCallback: _ => true, name: "STOP_BIT")
                .WithValueField(1, 11, FieldMode.Read, valueProviderCallback: _ => Manufacturer, name: "MANUFACTURER")
                .WithValueField(12, 16, FieldMode.Read, valueProviderCallback: _ => Part, name: "PART")
                .WithValueField(28, 4, FieldMode.Read, valueProviderCallback: _ => revision, name: "REVISION");

            Registers.PackageSelect.Define(this)
                .WithFlag(0, FieldMode.Read, valueProviderCallback: _ => qfn60, name: "PACKAGE_SEL")
                .WithReservedBits(1, 31);

            Registers.Platform.Define(this)
                .WithFlag(0, FieldMode.Read, name: "FPGA")
                .WithFlag(1, FieldMode.Read, valueProviderCallback: _ => true, name: "ASIC")
                .WithFlag(2, FieldMode.Read, name: "HDLSIM")
                .WithFlag(3, FieldMode.Read, name: "BATCHSIM")
                .WithFlag(4, FieldMode.Read, name: "GATESIM")
                .WithReservedBits(5, 27);

            // The revision of the chip's source; the model has none to give.
            Registers.GitRef.Define(this)
                .WithValueField(0, 32, FieldMode.Read, name: "GITREF_RP2350");
        }

        // Raspberry Pi's JEDEC code, and the RP2350's part number.
        private const uint Manufacturer = 0x493;
        private const uint Part = 0x0004;

        private enum Registers : long
        {
            ChipId = 0x00,
            PackageSelect = 0x04,
            Platform = 0x08,
            GitRef = 0x14,
        }
    }

    // TBMAN: tells software whether it runs on silicon, on an FPGA or in a
    // simulation of the chip's logic. The model answers "silicon", so that
    // software takes the paths it would on a board.
    public class RP2350_TBMAN : RP2350_RegisterBlock
    {
        public RP2350_TBMAN(IMachine machine) : base(machine)
        {
            Registers.Platform.Define(this)
                .WithFlag(0, FieldMode.Read, valueProviderCallback: _ => true, name: "ASIC")
                .WithFlag(1, FieldMode.Read, name: "FPGA")
                .WithFlag(2, FieldMode.Read, name: "HDLSIM")
                .WithReservedBits(3, 29);
        }

        private enum Registers : long
        {
            Platform = 0x0,
        }
    }
}
