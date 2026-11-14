//
// Copyright (c) 2026 Muhammad Waleed Badar
// SPDX-License-Identifier: Apache-2.0
//
using System.Collections.Generic;
using Antmicro.Renode.Core;
using Antmicro.Renode.Core.Structure.Registers;
using Antmicro.Renode.Peripherals.Bus;

namespace Antmicro.Renode.Peripherals.Miscellaneous
{
    // Every RP2350 register block is decoded four times: the registers, then
    // aliases that XOR, set and clear bits on write.
    public static class RP2350_AtomicAccess
    {
        public static uint Read(DoubleWordRegisterCollection registers, long offset)
        {
            return registers.Read(offset & RegisterMask);
        }

        public static void Write(DoubleWordRegisterCollection registers, long offset, uint value, ICollection<long> writeOneToClear = null)
        {
            var register = offset & RegisterMask;
            var alias = (Alias)((offset >> AliasShift) & 0x3);

            // A write-one-to-clear register takes the value as written:
            // merging the current value in would acknowledge bits the caller
            // never named.
            if(alias != Alias.None && (writeOneToClear == null || !writeOneToClear.Contains(register)))
            {
                var current = registers.Read(register);
                switch(alias)
                {
                case Alias.Xor:
                    value = current ^ value;
                    break;
                case Alias.Set:
                    value = current | value;
                    break;
                case Alias.Clear:
                    value = current & ~value;
                    break;
                }
            }
            registers.Write(register, value);
        }

        // Registers and their three aliases.
        public const long BlockSize = 0x4000;

        private const long RegisterMask = 0xFFF;
        private const int AliasShift = 12;

        private enum Alias
        {
            None = 0,
            Xor = 1,
            Set = 2,
            Clear = 3,
        }
    }

    // Common part of the register blocks. Both cores access them, so every
    // access takes the block lock.
    [AllowedTranslations(AllowedTranslation.ByteToDoubleWord | AllowedTranslation.WordToDoubleWord)]
    public abstract class RP2350_RegisterBlock : IDoubleWordPeripheral, IKnownSize, IProvidesRegisterCollection<DoubleWordRegisterCollection>
    {
        public virtual void Reset()
        {
            lock(locker)
            {
                RegistersCollection.Reset();
            }
        }

        public uint ReadDoubleWord(long offset)
        {
            lock(locker)
            {
                return RP2350_AtomicAccess.Read(RegistersCollection, offset);
            }
        }

        public void WriteDoubleWord(long offset, uint value)
        {
            lock(locker)
            {
                RP2350_AtomicAccess.Write(RegistersCollection, offset, value, writeOneToClear);
            }
        }

        public long Size => RP2350_AtomicAccess.BlockSize;

        public DoubleWordRegisterCollection RegistersCollection { get; }

        protected RP2350_RegisterBlock(IMachine machine)
        {
            this.machine = machine;
            RegistersCollection = new DoubleWordRegisterCollection(this);
        }

        protected void MarkWriteOneToClear(long register)
        {
            writeOneToClear.Add(register);
        }

        protected readonly IMachine machine;
        protected readonly object locker = new object();

        private readonly HashSet<long> writeOneToClear = new HashSet<long>();
    }
}
