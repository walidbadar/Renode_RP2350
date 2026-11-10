//
// Copyright (c) 2026 Muhammad Waleed Badar
// SPDX-License-Identifier: GPL-3.0-only
//
using Antmicro.Renode.Core;
using Antmicro.Renode.Peripherals.Bus;

namespace Antmicro.Renode.Peripherals.Miscellaneous
{
    // The XOR, set and clear aliases of a peripheral whose own model knows
    // nothing about them, such as the stock PL011 that stands in for the
    // RP2350 UARTs. Register it 4 kB above the peripheral it wraps.
    //
    // This is how the chip does it for the Arm and Synopsys blocks too: a
    // bridge in front of the peripheral turns one aliased write into a read
    // and a write. A register that changes when it is read is disturbed by
    // that here as it is there.
    [AllowedTranslations(AllowedTranslation.ByteToDoubleWord | AllowedTranslation.WordToDoubleWord)]
    public class RP2350_AtomicAlias : IDoubleWordPeripheral, IKnownSize
    {
        public RP2350_AtomicAlias(IMachine machine, IDoubleWordPeripheral target)
        {
            this.target = target;
        }

        public void Reset()
        {
            // Nothing of its own to reset.
        }

        public uint ReadDoubleWord(long offset)
        {
            return target.ReadDoubleWord(offset & RegisterMask);
        }

        public void WriteDoubleWord(long offset, uint value)
        {
            var register = offset & RegisterMask;
            lock(locker)
            {
                var current = target.ReadDoubleWord(register);
                switch(offset >> AliasShift)
                {
                case 0:
                    value = current ^ value;
                    break;
                case 1:
                    value = current | value;
                    break;
                default:
                    value = current & ~value;
                    break;
                }
                target.WriteDoubleWord(register, value);
            }
        }

        public long Size => 0x3000;

        private readonly IDoubleWordPeripheral target;
        private readonly object locker = new object();

        private const long RegisterMask = 0xFFF;
        private const int AliasShift = 12;
    }
}
