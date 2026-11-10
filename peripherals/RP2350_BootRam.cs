//
// Copyright (c) 2026 Muhammad Waleed Badar
// SPDX-License-Identifier: Apache-2.0
//
using Antmicro.Renode.Core;
using Antmicro.Renode.Core.Structure.Registers;
using Antmicro.Renode.Peripherals.Bus;

namespace Antmicro.Renode.Peripherals.Miscellaneous
{
    // BOOTRAM: 1 kB of RAM set aside for the boot ROM, which keeps its stack
    // there so that booting never disturbs main SRAM, followed by a few
    // registers: two words whose bits can be set but not cleared, and eight
    // locks that work like the SIO spinlocks and keep the two cores out of
    // each other's way inside the ROM.
    [AllowedTranslations(AllowedTranslation.ByteToDoubleWord | AllowedTranslation.WordToDoubleWord)]
    public class RP2350_BootRam : IDoubleWordPeripheral, IKnownSize, IProvidesRegisterCollection<DoubleWordRegisterCollection>
    {
        public RP2350_BootRam(IMachine machine)
        {
            RegistersCollection = new DoubleWordRegisterCollection(this);

            Registers.WriteOnce0.DefineMany(this, 2, (register, index) =>
            {
                register.WithValueField(0, 32, valueProviderCallback: _ => writeOnce[index],
                    writeCallback: (_, value) => writeOnce[index] |= (uint)value, name: "WRITE_ONCE");
            });

            Registers.BootlockStatus.Define(this)
                .WithValueField(0, NumberOfLocks, FieldMode.Read, valueProviderCallback: _ => ~claimed & AllLocks, name: "BOOTLOCK_STAT")
                .WithReservedBits(NumberOfLocks, 32 - NumberOfLocks);

            Registers.Bootlock0.DefineMany(this, NumberOfLocks, (register, index) =>
            {
                var bit = 1u << index;
                register.WithValueField(0, 32,
                    valueProviderCallback: _ =>
                    {
                        // Reading claims the lock, and says so by returning
                        // nonzero; a lock that is already held reads as zero.
                        if((claimed & bit) != 0)
                        {
                            return 0;
                        }
                        claimed |= bit;
                        return bit;
                    },
                    writeCallback: (_, __) => claimed &= ~bit,
                    name: "BOOTLOCK");
            });
        }

        // The RAM keeps its contents, as RAM does across a reset.
        public void Reset()
        {
            lock(locker)
            {
                RegistersCollection.Reset();
                writeOnce[0] = 0;
                writeOnce[1] = 0;
                claimed = 0;
            }
        }

        public uint ReadDoubleWord(long offset)
        {
            lock(locker)
            {
                return offset < RamSize ? ram[offset / 4] : RegistersCollection.Read(offset);
            }
        }

        public void WriteDoubleWord(long offset, uint value)
        {
            lock(locker)
            {
                if(offset < RamSize)
                {
                    ram[offset / 4] = value;
                }
                else
                {
                    RegistersCollection.Write(offset, value);
                }
            }
        }

        public long Size => 0x1000;

        public DoubleWordRegisterCollection RegistersCollection { get; }

        private uint claimed;

        private readonly uint[] ram = new uint[RamSize / 4];
        private readonly uint[] writeOnce = new uint[2];
        private readonly object locker = new object();

        private const long RamSize = 0x400;
        private const int NumberOfLocks = 8;
        private const uint AllLocks = (1u << NumberOfLocks) - 1;

        private enum Registers : long
        {
            WriteOnce0 = 0x800,
            BootlockStatus = 0x808,
            Bootlock0 = 0x80C,
        }
    }
}
