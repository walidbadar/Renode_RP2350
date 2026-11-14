//
// Copyright (c) 2026 Muhammad Waleed Badar
// SPDX-License-Identifier: Apache-2.0
//
using Antmicro.Renode.Core;
using Antmicro.Renode.Core.Structure.Registers;

namespace Antmicro.Renode.Peripherals.Miscellaneous
{
    // SYSINFO: identifies the chip, its package and its silicon revision.
    public class RP2350_SysInfo : RP2350_RegisterBlock
    {
        // Revision 2 is the A2 stepping. qfn60 selects the RP2350A, with 30
        // GPIOs, over the RP2350B, with 48.
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

            Registers.GitRef.Define(this)
                .WithValueField(0, 32, FieldMode.Read, name: "GITREF_RP2350");
        }

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

    // TBMAN: tells software that it runs on silicon and not in a simulation.
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
