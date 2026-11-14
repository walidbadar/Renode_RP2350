//
// Copyright (c) 2026 Muhammad Waleed Badar
// SPDX-License-Identifier: Apache-2.0
//
using System;
using System.Collections.Generic;
using Antmicro.Renode.Core;
using Antmicro.Renode.Core.Structure.Registers;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;
using Antmicro.Renode.Peripherals.CPU;
using Antmicro.Renode.Peripherals.GPIOPort;

namespace Antmicro.Renode.Peripherals.Miscellaneous
{
    // SIO: tells a core which one it is, and holds the mailbox FIFOs and the
    // doorbells between the cores, the spinlocks and the GPIOs. Most registers
    // depend on the core that asks. An access from outside a CPU, such as from
    // the Monitor, counts as one from core 0.
    //
    // Not modelled: the interpolators, the TMDS encoder, the RISC-V timer and
    // soft interrupts, and the Non-secure copy of the block.
    [AllowedTranslations(AllowedTranslation.ByteToDoubleWord | AllowedTranslation.WordToDoubleWord)]
    public class RP2350_SIO : IDoubleWordPeripheral, IKnownSize, IProvidesRegisterCollection<DoubleWordRegisterCollection>
    {
        public RP2350_SIO(IMachine machine, RP2350_IOBank0 gpio = null)
        {
            this.machine = machine;
            this.gpio = gpio;

            for(var core = 0; core < NumberOfCores; core++)
            {
                receiveFifo[core] = new Queue<uint>();
            }
            FifoIRQ0 = new GPIO();
            FifoIRQ1 = new GPIO();
            DoorbellIRQ0 = new GPIO();
            DoorbellIRQ1 = new GPIO();

            RegistersCollection = new DoubleWordRegisterCollection(this);
            DefineRegisters();
            Reset();
        }

        public void Reset()
        {
            lock(locker)
            {
                RegistersCollection.Reset();
                for(var core = 0; core < NumberOfCores; core++)
                {
                    receiveFifo[core].Clear();
                    writeOverflow[core] = false;
                    readOnEmpty[core] = false;
                    doorbells[core] = 0;
                    peripheralsNonSecure[core] = 0;
                }
                spinlocks = 0;
                outputs = 0;
                enables = 0;
                gpio?.SetSioOutputs(outputs, enables);
                UpdateInterrupts();
            }
        }

        public uint ReadDoubleWord(long offset)
        {
            lock(locker)
            {
                return RegistersCollection.Read(offset);
            }
        }

        public void WriteDoubleWord(long offset, uint value)
        {
            lock(locker)
            {
                RegistersCollection.Write(offset, value);
            }
        }

        public long Size => 0x1000;

        public DoubleWordRegisterCollection RegistersCollection { get; }

        public GPIO FifoIRQ0 { get; }
        public GPIO FifoIRQ1 { get; }

        public GPIO DoorbellIRQ0 { get; }
        public GPIO DoorbellIRQ1 { get; }

        public ulong GpioOutputs
        {
            get
            {
                lock(locker)
                {
                    return outputs;
                }
            }
        }

        public ulong GpioOutputEnables
        {
            get
            {
                lock(locker)
                {
                    return enables;
                }
            }
        }

        // Without an IO bank, the pins read back what the SIO drives.
        public ulong GpioInputs
        {
            get
            {
                lock(locker)
                {
                    return gpio != null ? gpio.ReadPins() : (outputs & enables);
                }
            }
        }

        // The GPIO coprocessor of each core drives the same registers through
        // these two.
        public void UpdateGpioOutputs(Func<ulong, ulong> change)
        {
            lock(locker)
            {
                outputs = change(outputs) & GpioMask;
                gpio?.SetSioOutputs(outputs, enables);
            }
        }

        public void UpdateGpioOutputEnables(Func<ulong, ulong> change)
        {
            lock(locker)
            {
                enables = change(enables) & GpioMask;
                gpio?.SetSioOutputs(outputs, enables);
            }
        }

        private void DefineRegisters()
        {
            Registers.CpuId.Define(this)
                .WithValueField(0, 32, FieldMode.Read, valueProviderCallback: _ => (uint)CurrentCore, name: "CPUID");

            Registers.GpioIn.Define(this)
                .WithValueField(0, 32, FieldMode.Read, valueProviderCallback: _ => (uint)GpioInputs, name: "GPIO_IN");
            Registers.GpioHiIn.Define(this)
                .WithValueField(0, 32, FieldMode.Read, valueProviderCallback: _ => (uint)(GpioInputs >> 32), name: "GPIO_HI_IN");

            DefineGpioRegisters(Registers.GpioOut, () => outputs, UpdateGpioOutputs, "GPIO_OUT");
            DefineGpioRegisters(Registers.GpioOe, () => enables, UpdateGpioOutputEnables, "GPIO_OE");

            Registers.FifoStatus.Define(this)
                .WithFlag(0, FieldMode.Read, valueProviderCallback: _ => receiveFifo[CurrentCore].Count > 0, name: "VLD")
                .WithFlag(1, FieldMode.Read, valueProviderCallback: _ => receiveFifo[OtherCore].Count < FifoDepth, name: "RDY")
                // The two error flags are sticky and cleared by writing 1.
                .WithFlag(2,
                    valueProviderCallback: _ => writeOverflow[CurrentCore],
                    writeCallback: (_, value) =>
                    {
                        if(value)
                        {
                            writeOverflow[CurrentCore] = false;
                        }
                    }, name: "WOF")
                .WithFlag(3,
                    valueProviderCallback: _ => readOnEmpty[CurrentCore],
                    writeCallback: (_, value) =>
                    {
                        if(value)
                        {
                            readOnEmpty[CurrentCore] = false;
                        }
                    }, name: "ROE")
                // Drivers clear the flags by writing all ones.
                .WithValueField(4, 28, FieldMode.Write, name: "UNUSED")
                .WithWriteCallback((_, __) => UpdateInterrupts());

            Registers.FifoWrite.Define(this)
                .WithValueField(0, 32, FieldMode.Write, writeCallback: (_, value) =>
                {
                    var fifo = receiveFifo[OtherCore];
                    if(fifo.Count < FifoDepth)
                    {
                        fifo.Enqueue((uint)value);
                    }
                    else
                    {
                        // The FIFO ignores the write and flags it.
                        writeOverflow[CurrentCore] = true;
                    }
                    UpdateInterrupts();
                }, name: "FIFO_WR");

            Registers.FifoRead.Define(this)
                .WithValueField(0, 32, FieldMode.Read, valueProviderCallback: _ =>
                {
                    var fifo = receiveFifo[CurrentCore];
                    var value = 0u;
                    if(fifo.Count > 0)
                    {
                        value = fifo.Dequeue();
                    }
                    else
                    {
                        // The FIFO ignores the read and flags it.
                        readOnEmpty[CurrentCore] = true;
                    }
                    UpdateInterrupts();
                    return value;
                }, name: "FIFO_RD");

            Registers.SpinlockStatus.Define(this)
                .WithValueField(0, 32, FieldMode.Read, valueProviderCallback: _ => spinlocks, name: "SPINLOCK_ST");

            Registers.Spinlock0.DefineMany(this, NumberOfSpinlocks, (register, index) =>
            {
                var bit = 1u << index;
                register.WithValueField(0, 32,
                    valueProviderCallback: _ =>
                    {
                        // Reading claims the lock; a held lock reads as 0.
                        if((spinlocks & bit) != 0)
                        {
                            return 0;
                        }
                        spinlocks |= bit;
                        return bit;
                    },
                    writeCallback: (_, __) => spinlocks &= ~bit,
                    name: "SPINLOCK");
            });

            // The two OUT registers show and change the doorbells of the
            // other core, the two IN registers those of the core asking.
            Registers.DoorbellOutSet.Define(this)
                .WithValueField(0, 8, valueProviderCallback: _ => doorbells[OtherCore],
                    writeCallback: (_, value) => doorbells[OtherCore] |= (uint)value, name: "DOORBELL_OUT_SET")
                .WithReservedBits(8, 24)
                .WithWriteCallback((_, __) => UpdateInterrupts());
            Registers.DoorbellOutClear.Define(this)
                .WithValueField(0, 8, valueProviderCallback: _ => doorbells[OtherCore],
                    writeCallback: (_, value) => doorbells[OtherCore] &= ~(uint)value, name: "DOORBELL_OUT_CLR")
                .WithReservedBits(8, 24)
                .WithWriteCallback((_, __) => UpdateInterrupts());
            Registers.DoorbellInSet.Define(this)
                .WithValueField(0, 8, valueProviderCallback: _ => doorbells[CurrentCore],
                    writeCallback: (_, value) => doorbells[CurrentCore] |= (uint)value, name: "DOORBELL_IN_SET")
                .WithReservedBits(8, 24)
                .WithWriteCallback((_, __) => UpdateInterrupts());
            Registers.DoorbellInClear.Define(this)
                .WithValueField(0, 8, valueProviderCallback: _ => doorbells[CurrentCore],
                    writeCallback: (_, value) => doorbells[CurrentCore] &= ~(uint)value, name: "DOORBELL_IN_CLR")
                .WithReservedBits(8, 24)
                .WithWriteCallback((_, __) => UpdateInterrupts());

            // Stored only: there is no Non-secure SIO.
            Registers.PeripheralsNonSecure.Define(this)
                .WithValueField(0, 6, valueProviderCallback: _ => peripheralsNonSecure[CurrentCore],
                    writeCallback: (_, value) => peripheralsNonSecure[CurrentCore] = (uint)value & 0x23, name: "PERI_NONSEC")
                .WithReservedBits(6, 26);
        }

        // A value register for each half of the 48 pins, each followed by
        // write-only registers that set, clear and toggle bits in it.
        private void DefineGpioRegisters(Registers first, Func<ulong> getter, Action<Func<ulong, ulong>> update, string name)
        {
            for(var half = 0; half < 2; half++)
            {
                var shift = half * 32;
                var mask = 0xFFFFFFFFUL << shift;
                var suffix = half == 0 ? "" : "_HI";
                var offset = (long)first + half * 4;

                RegistersCollection.DefineRegister(offset)
                    .WithValueField(0, 32, valueProviderCallback: _ => (getter() & mask) >> shift,
                        writeCallback: (_, value) => update(current => (current & ~mask) | (value << shift)), name: name + suffix);
                RegistersCollection.DefineRegister(offset + 0x08)
                    .WithValueField(0, 32, FieldMode.Write,
                        writeCallback: (_, value) => update(current => current | (value << shift)), name: name + suffix + "_SET");
                RegistersCollection.DefineRegister(offset + 0x10)
                    .WithValueField(0, 32, FieldMode.Write,
                        writeCallback: (_, value) => update(current => current & ~(value << shift)), name: name + suffix + "_CLR");
                RegistersCollection.DefineRegister(offset + 0x18)
                    .WithValueField(0, 32, FieldMode.Write,
                        writeCallback: (_, value) => update(current => current ^ (value << shift)), name: name + suffix + "_XOR");
            }
        }

        // The lines of the core making the access change at once. Those of the
        // other core change at the next point where Renode has the cores in
        // step: its NVIC is not prepared for an interrupt that arrives from
        // another thread while the core runs.
        private void UpdateInterrupts()
        {
            if(!machine.GetSystemBus(this).TryGetCurrentCPU(out var _))
            {
                UpdateInterrupts(0);
                UpdateInterrupts(1);
                return;
            }

            var other = OtherCore;
            UpdateInterrupts(CurrentCore);
            machine.LocalTimeSource.ExecuteInNearestSyncedState(_ =>
            {
                lock(locker)
                {
                    UpdateInterrupts(other);
                }
            });
        }

        private void UpdateInterrupts(int core)
        {
            var fifo = receiveFifo[core].Count > 0 || writeOverflow[core] || readOnEmpty[core];
            var doorbell = doorbells[core] != 0;
            (core == 0 ? FifoIRQ0 : FifoIRQ1).Set(fifo);
            (core == 0 ? DoorbellIRQ0 : DoorbellIRQ1).Set(doorbell);
        }

        private int CurrentCore
        {
            get
            {
                if(machine.GetSystemBus(this).TryGetCurrentCPU(out var cpu))
                {
                    if(cpu.MultiprocessingId < NumberOfCores)
                    {
                        return (int)cpu.MultiprocessingId;
                    }
                    this.Log(LogLevel.Error, "Accessed by a CPU with id {0}, but the RP2350 has cores 0 and 1 only; treating it as core 0", cpu.MultiprocessingId);
                }
                return 0;
            }
        }

        private int OtherCore => 1 - CurrentCore;

        private uint spinlocks;
        private ulong outputs;
        private ulong enables;

        private readonly IMachine machine;
        private readonly RP2350_IOBank0 gpio;
        private readonly object locker = new object();

        // Indexed by the core that reads the FIFO: receiveFifo[1] is the
        // mailbox from core 0 to core 1.
        private readonly Queue<uint>[] receiveFifo = new Queue<uint>[NumberOfCores];
        private readonly bool[] writeOverflow = new bool[NumberOfCores];
        private readonly bool[] readOnEmpty = new bool[NumberOfCores];
        private readonly uint[] doorbells = new uint[NumberOfCores];
        private readonly uint[] peripheralsNonSecure = new uint[NumberOfCores];

        private const int NumberOfCores = 2;
        private const int FifoDepth = 4;
        private const uint NumberOfSpinlocks = 32;
        private const ulong GpioMask = (1UL << RP2350_IOBank0.NumberOfPins) - 1;

        private enum Registers : long
        {
            CpuId = 0x000,
            GpioIn = 0x004,
            GpioHiIn = 0x008,
            GpioOut = 0x010,
            GpioOe = 0x030,
            FifoStatus = 0x050,
            FifoWrite = 0x054,
            FifoRead = 0x058,
            SpinlockStatus = 0x05C,
            Spinlock0 = 0x100,
            DoorbellOutSet = 0x180,
            DoorbellOutClear = 0x184,
            DoorbellInSet = 0x188,
            DoorbellInClear = 0x18C,
            PeripheralsNonSecure = 0x190,
        }
    }
}
