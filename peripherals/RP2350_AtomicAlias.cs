//
// Copyright (c) 2026 Muhammad Waleed Badar
// SPDX-License-Identifier: Apache-2.0
//
using Antmicro.Renode.Core;
using Antmicro.Renode.Peripherals.Bus;

namespace Antmicro.Renode.Peripherals.Miscellaneous
{
    // The XOR, set and clear aliases of a peripheral whose own model lacks
    // them, such as the PL011 of the UARTs. Register it 4 kB above the
    // peripheral it wraps. An aliased write is a read and a write of the
    // wrapped register.
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
