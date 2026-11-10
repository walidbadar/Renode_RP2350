//
// Copyright (c) 2026 Muhammad Waleed Badar
// SPDX-License-Identifier: GPL-3.0-only
//
using System.Collections.Generic;
using Antmicro.Renode.Core;
using Antmicro.Renode.Core.Structure.Registers;
using Antmicro.Renode.Peripherals.Bus;
using Antmicro.Renode.Peripherals.Miscellaneous;

namespace Antmicro.Renode.Peripherals.GPIOPort
{
    // IO_BANK0: function select and interrupts for the 48 user GPIOs. This is
    // where the pins live in the model: anything wired to a pin in the
    // platform description connects here, and the SIO reaches the outside
    // world through it.
    //
    // Only the SIO function drives a pin; the other functions are modelled at
    // the level of their peripherals, not their pins. GPIO interrupts are
    // routed twice, once to each core, with separate enables.
    [AllowedTranslations(AllowedTranslation.ByteToDoubleWord | AllowedTranslation.WordToDoubleWord)]
    public class RP2350_IOBank0 : BaseGPIOPort, IDoubleWordPeripheral, IKnownSize, IProvidesRegisterCollection<DoubleWordRegisterCollection>
    {
        public RP2350_IOBank0(IMachine machine, RP2350_Pads pads = null) : base(machine, NumberOfPins)
        {
            this.pads = pads;
            Proc0IRQ = new GPIO();
            Proc1IRQ = new GPIO();
            RegistersCollection = new DoubleWordRegisterCollection(this);
            DefineRegisters();

            if(pads != null)
            {
                pads.PadChanged += pad =>
                {
                    lock(locker)
                    {
                        UpdatePin(pad);
                        UpdateInterrupts();
                    }
                };
            }
        }

        public override void Reset()
        {
            lock(locker)
            {
                // Levels driven from outside are not this block's to forget,
                // so the inherited reset, which clears them, is not used.
                RegistersCollection.Reset();
                sioOutputs = 0;
                sioEnables = 0;
                for(var pin = 0; pin < NumberOfPins; pin++)
                {
                    padOutput[pin] = false;
                    padEnable[pin] = false;
                    edgeLow[pin] = false;
                    edgeHigh[pin] = false;
                    Connections[pin].Unset();
                    UpdatePin(pin, detectEdges: false);
                }
                for(var target = 0; target < NumberOfTargets; target++)
                {
                    for(var index = 0; index < NumberOfInterruptRegisters; index++)
                    {
                        enabled[target, index] = 0;
                        forced[target, index] = 0;
                    }
                }
                UpdateInterrupts();
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

        public override void OnGPIO(int number, bool value)
        {
            lock(locker)
            {
                base.OnGPIO(number, value);
                if(number >= 0 && number < NumberOfPins)
                {
                    UpdatePin(number);
                    UpdateInterrupts();
                }
            }
        }

        // Output levels and output enables of the SIO, one bit per pin.
        public void SetSioOutputs(ulong outputs, ulong enables)
        {
            lock(locker)
            {
                var changed = (outputs ^ sioOutputs) | (enables ^ sioEnables);
                sioOutputs = outputs;
                sioEnables = enables;
                for(var pin = 0; pin < NumberOfPins; pin++)
                {
                    if((changed & (1UL << pin)) != 0)
                    {
                        UpdatePin(pin);
                    }
                }
                UpdateInterrupts();
            }
        }

        // Levels the peripherals see on the pins, one bit per pin.
        public ulong ReadPins()
        {
            lock(locker)
            {
                var result = 0UL;
                for(var pin = 0; pin < NumberOfPins; pin++)
                {
                    if(toPeripherals[pin])
                    {
                        result |= 1UL << pin;
                    }
                }
                return result;
            }
        }

        public long Size => RP2350_AtomicAccess.BlockSize;

        public DoubleWordRegisterCollection RegistersCollection { get; }

        public GPIO Proc0IRQ { get; }
        public GPIO Proc1IRQ { get; }

        public const int NumberOfPins = 48;

        private void DefineRegisters()
        {
            Registers.Gpio0Status.DefineMany(this, NumberOfPins, (register, pin) =>
            {
                register
                    .WithReservedBits(0, 9)
                    .WithFlag(9, FieldMode.Read, valueProviderCallback: _ => padOutput[pin], name: "OUTTOPAD")
                    .WithReservedBits(10, 3)
                    .WithFlag(13, FieldMode.Read, valueProviderCallback: _ => padEnable[pin], name: "OETOPAD")
                    .WithReservedBits(14, 3)
                    .WithFlag(17, FieldMode.Read, valueProviderCallback: _ => fromPad[pin], name: "INFROMPAD")
                    .WithReservedBits(18, 8)
                    .WithFlag(26, FieldMode.Read, valueProviderCallback: _ => toInterrupt[pin], name: "IRQTOPROC")
                    .WithReservedBits(27, 5);
            }, stepInBytes: 8);

            Registers.Gpio0Control.DefineMany(this, NumberOfPins, (register, pin) =>
            {
                register
                    .WithValueField(0, 5, out functionSelect[pin], name: "FUNCSEL")
                    .WithReservedBits(5, 7)
                    .WithValueField(12, 2, out outputOverride[pin], name: "OUTOVER")
                    .WithValueField(14, 2, out enableOverride[pin], name: "OEOVER")
                    .WithValueField(16, 2, out inputOverride[pin], name: "INOVER")
                    .WithReservedBits(18, 10)
                    .WithValueField(28, 2, out interruptOverride[pin], name: "IRQOVER")
                    .WithReservedBits(30, 2)
                    .WithWriteCallback((_, __) =>
                    {
                        UpdatePin(pin);
                        UpdateInterrupts();
                    });
            }, stepInBytes: 8, resetValue: FunctionNull);

            // Which pins are interrupting each destination, one bit per pin.
            // Nothing is ever routed to the Non-secure side here.
            for(var target = 0; target < NumberOfTargets; target++)
            {
                var t = target;
                var summary = (long)Registers.InterruptSummaryProc0Secure0 + target * 0x10;
                RegistersCollection.DefineRegister(summary)
                    .WithValueField(0, 32, FieldMode.Read, valueProviderCallback: _ => (uint)Summary(t), name: "IRQSUMMARY_SECURE0");
                RegistersCollection.DefineRegister(summary + 0x4)
                    .WithValueField(0, 16, FieldMode.Read, valueProviderCallback: _ => (uint)(Summary(t) >> 32), name: "IRQSUMMARY_SECURE1")
                    .WithReservedBits(16, 16);
                RegistersCollection.DefineRegister(summary + 0x8)
                    .WithValueField(0, 32, FieldMode.Read, name: "IRQSUMMARY_NONSECURE0");
                RegistersCollection.DefineRegister(summary + 0xC)
                    .WithValueField(0, 32, FieldMode.Read, name: "IRQSUMMARY_NONSECURE1");
            }

            Registers.RawInterrupts0.DefineMany(this, NumberOfInterruptRegisters, (register, index) =>
            {
                register
                    .WithValueField(0, 32, valueProviderCallback: _ => RawInterrupts(index),
                        writeCallback: (_, value) =>
                        {
                            // Only the edge events latch; the level bits follow the pin.
                            for(var slot = 0; slot < PinsPerRegister; slot++)
                            {
                                var pin = index * PinsPerRegister + slot;
                                var bits = (value >> (slot * BitsPerPin)) & 0xF;
                                if((bits & EdgeLowBit) != 0)
                                {
                                    edgeLow[pin] = false;
                                }
                                if((bits & EdgeHighBit) != 0)
                                {
                                    edgeHigh[pin] = false;
                                }
                            }
                            UpdateInterrupts();
                        }, name: "INTR");
                writeOneToClear.Add((long)Registers.RawInterrupts0 + index * 4);
            });

            for(var target = 0; target < NumberOfTargets; target++)
            {
                var t = target;
                var block = (long)Registers.Proc0InterruptEnable0 + target * TargetStride;

                for(var index = 0; index < NumberOfInterruptRegisters; index++)
                {
                    var i = index;
                    RegistersCollection.DefineRegister(block + index * 4)
                        .WithValueField(0, 32, valueProviderCallback: _ => enabled[t, i],
                            writeCallback: (_, value) =>
                            {
                                enabled[t, i] = (uint)value;
                                UpdateInterrupts();
                            }, name: "INTE");
                    RegistersCollection.DefineRegister(block + InterruptForceOffset + index * 4)
                        .WithValueField(0, 32, valueProviderCallback: _ => forced[t, i],
                            writeCallback: (_, value) =>
                            {
                                forced[t, i] = (uint)value;
                                UpdateInterrupts();
                            }, name: "INTF");
                    RegistersCollection.DefineRegister(block + InterruptStatusOffset + index * 4)
                        .WithValueField(0, 32, FieldMode.Read, valueProviderCallback: _ => InterruptStatus(t, i), name: "INTS");
                }
            }
        }

        private void UpdatePin(int pin, bool detectEdges = true)
        {
            var isSio = functionSelect[pin].Value == FunctionSio;
            var output = Override(isSio && (sioOutputs & (1UL << pin)) != 0, outputOverride[pin].Value);
            var enable = Override(isSio && (sioEnables & (1UL << pin)) != 0, enableOverride[pin].Value);

            // An isolated pad keeps driving what it drove when it was isolated.
            if(pads == null || !pads.IsIsolated(pin))
            {
                padOutput[pin] = output;
                padEnable[pin] = enable;
                Connections[pin].Set(enable && output);
            }

            var level = padEnable[pin] ? padOutput[pin] : State[pin];
            fromPad[pin] = level && (pads == null || pads.IsInputEnabled(pin));
            toPeripherals[pin] = Override(fromPad[pin], inputOverride[pin].Value);

            var interrupt = Override(fromPad[pin], interruptOverride[pin].Value);
            if(interrupt != toInterrupt[pin])
            {
                toInterrupt[pin] = interrupt;
                if(detectEdges)
                {
                    if(interrupt)
                    {
                        edgeHigh[pin] = true;
                    }
                    else
                    {
                        edgeLow[pin] = true;
                    }
                }
            }
        }

        private void UpdateInterrupts()
        {
            Proc0IRQ.Set(Summary(0) != 0);
            Proc1IRQ.Set(Summary(1) != 0);
        }

        private uint RawInterrupts(int index)
        {
            var result = 0u;
            for(var slot = 0; slot < PinsPerRegister; slot++)
            {
                var pin = index * PinsPerRegister + slot;
                var bits = toInterrupt[pin] ? LevelHighBit : LevelLowBit;
                if(edgeLow[pin])
                {
                    bits |= EdgeLowBit;
                }
                if(edgeHigh[pin])
                {
                    bits |= EdgeHighBit;
                }
                result |= bits << (slot * BitsPerPin);
            }
            return result;
        }

        private uint InterruptStatus(int target, int index)
        {
            return (RawInterrupts(index) | forced[target, index]) & enabled[target, index];
        }

        private ulong Summary(int target)
        {
            var result = 0UL;
            for(var index = 0; index < NumberOfInterruptRegisters; index++)
            {
                var status = InterruptStatus(target, index);
                for(var slot = 0; slot < PinsPerRegister; slot++)
                {
                    if(((status >> (slot * BitsPerPin)) & 0xF) != 0)
                    {
                        result |= 1UL << (index * PinsPerRegister + slot);
                    }
                }
            }
            return result;
        }

        // The four settings every override field shares.
        private static bool Override(bool value, ulong mode)
        {
            switch(mode)
            {
            case 1:
                return !value;
            case 2:
                return false;
            case 3:
                return true;
            default:
                return value;
            }
        }

        private ulong sioOutputs;
        private ulong sioEnables;

        private readonly RP2350_Pads pads;
        private readonly object locker = new object();
        private readonly HashSet<long> writeOneToClear = new HashSet<long>();

        private readonly IValueRegisterField[] functionSelect = new IValueRegisterField[NumberOfPins];
        private readonly IValueRegisterField[] outputOverride = new IValueRegisterField[NumberOfPins];
        private readonly IValueRegisterField[] enableOverride = new IValueRegisterField[NumberOfPins];
        private readonly IValueRegisterField[] inputOverride = new IValueRegisterField[NumberOfPins];
        private readonly IValueRegisterField[] interruptOverride = new IValueRegisterField[NumberOfPins];

        private readonly bool[] padOutput = new bool[NumberOfPins];
        private readonly bool[] padEnable = new bool[NumberOfPins];
        private readonly bool[] fromPad = new bool[NumberOfPins];
        private readonly bool[] toPeripherals = new bool[NumberOfPins];
        private readonly bool[] toInterrupt = new bool[NumberOfPins];
        private readonly bool[] edgeLow = new bool[NumberOfPins];
        private readonly bool[] edgeHigh = new bool[NumberOfPins];

        // Per destination (core 0, core 1, wake from dormant) and register.
        private readonly uint[,] enabled = new uint[NumberOfTargets, NumberOfInterruptRegisters];
        private readonly uint[,] forced = new uint[NumberOfTargets, NumberOfInterruptRegisters];

        private const int NumberOfTargets = 3;
        private const int NumberOfInterruptRegisters = 6;
        private const int PinsPerRegister = 8;
        private const int BitsPerPin = 4;

        private const uint LevelLowBit = 0x1;
        private const uint LevelHighBit = 0x2;
        private const uint EdgeLowBit = 0x4;
        private const uint EdgeHighBit = 0x8;

        private const uint FunctionSio = 5;
        private const uint FunctionNull = 0x1F;

        // INTE, INTF and INTS of one destination follow each other.
        private const long TargetStride = 0x48;
        private const long InterruptForceOffset = 0x18;
        private const long InterruptStatusOffset = 0x30;

        private enum Registers : long
        {
            Gpio0Status = 0x000,
            Gpio0Control = 0x004,
            InterruptSummaryProc0Secure0 = 0x200,
            RawInterrupts0 = 0x230,
            Proc0InterruptEnable0 = 0x248,
        }
    }
}
