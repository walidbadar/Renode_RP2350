//
// Copyright (c) 2026 Muhammad Waleed Badar
// SPDX-License-Identifier: Apache-2.0
//
using Antmicro.Renode.Core;
using Antmicro.Renode.Core.Structure.Registers;
using Antmicro.Renode.Logging;

namespace Antmicro.Renode.Peripherals.Miscellaneous
{
    // RESETS: one reset line per peripheral block. A block that software
    // puts into reset is reset in the model too, whatever model sits at its
    // address, so firmware that hands over from one stage to the next finds
    // the hardware as clean as it would on silicon. Blocks come out of reset
    // at once.
    //
    // A block held in reset still answers on the bus here; on silicon it
    // would not.
    public class RP2350_Resets : RP2350_RegisterBlock
    {
        public RP2350_Resets(IMachine machine) : base(machine)
        {
            // Software names "every block but these" with masks that reach
            // into the unused top bits, so those take a write without
            // comment, and read as zero.
            Registers.Reset.Define(this, AllBlocks)
                .WithValueField(0, NumberOfBlocks, out reset, name: "RESET")
                .WithValueField(NumberOfBlocks, 32 - NumberOfBlocks, FieldMode.Write, name: "UNUSED")
                .WithChangeCallback((previous, current) =>
                {
                    var asserted = current & ~previous;
                    for(var block = 0; block < NumberOfBlocks; block++)
                    {
                        if((asserted & (1u << block)) != 0)
                        {
                            ResetBlock(block);
                        }
                    }
                });

            Registers.WatchdogSelect.Define(this)
                .WithValueField(0, NumberOfBlocks, name: "WDSEL")
                .WithValueField(NumberOfBlocks, 32 - NumberOfBlocks, FieldMode.Write, name: "UNUSED");

            Registers.ResetDone.Define(this)
                .WithValueField(0, NumberOfBlocks, FieldMode.Read,
                    valueProviderCallback: _ => AllBlocks & ~reset.Value, name: "RESET_DONE")
                .WithReservedBits(NumberOfBlocks, 32 - NumberOfBlocks);
        }

        private void ResetBlock(int block)
        {
            var address = blockAddresses[block];
            if(address == 0)
            {
                return;
            }

            var peripheral = machine.SystemBus.WhatPeripheralIsAt(address);
            if(peripheral == null || peripheral == this)
            {
                return;
            }
            this.Log(LogLevel.Debug, "Resetting the block at 0x{0:X8}", address);
            peripheral.Reset();
        }

        private readonly IValueRegisterField reset;

        // Where the block behind each bit of RESET lives; 0 for none.
        private readonly ulong[] blockAddresses =
        {
            0x400A0000, // ADC
            0x40068000, // BUSCTRL
            0x50000000, // DMA
            0x400C0000, // HSTX
            0x40090000, // I2C0
            0x40098000, // I2C1
            0x40028000, // IO_BANK0
            0x40030000, // IO_QSPI
            0,          // JTAG
            0x40038000, // PADS_BANK0
            0x40040000, // PADS_QSPI
            0x50200000, // PIO0
            0x50300000, // PIO1
            0x50400000, // PIO2
            0x40050000, // PLL_SYS
            0x40058000, // PLL_USB
            0x400A8000, // PWM
            0x400F8000, // SHA256
            0x40080000, // SPI0
            0x40088000, // SPI1
            0x40008000, // SYSCFG
            0x40000000, // SYSINFO
            0x40160000, // TBMAN
            0x400B0000, // TIMER0
            0x400B8000, // TIMER1
            0x400F0000, // TRNG
            0x40070000, // UART0
            0x40078000, // UART1
            0x50110000, // USBCTRL
        };

        private const int NumberOfBlocks = 29;
        private const uint AllBlocks = (1u << NumberOfBlocks) - 1;

        private enum Registers : long
        {
            Reset = 0x0,
            WatchdogSelect = 0x4,
            ResetDone = 0x8,
        }
    }
}
