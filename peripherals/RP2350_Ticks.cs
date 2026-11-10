//
// Copyright (c) 2026 Muhammad Waleed Badar
// SPDX-License-Identifier: Apache-2.0
//
using System;
using Antmicro.Renode.Core;
using Antmicro.Renode.Core.Structure.Registers;

namespace Antmicro.Renode.Peripherals.Miscellaneous
{
    // TICKS: six tick generators, each dividing clk_ref by a programmable
    // number of cycles. The timers and the watchdog count these ticks, so a
    // generator that is left disabled, or given the wrong cycle count, makes
    // its consumer stand still or run at the wrong rate, as it would on
    // silicon.
    public class RP2350_Ticks : RP2350_RegisterBlock
    {
        public RP2350_Ticks(IMachine machine, RP2350_Clocks clocks) : base(machine)
        {
            this.clocks = clocks;
            clocks.FrequenciesChanged += () => TicksChanged?.Invoke();

            Registers.Proc0Control.DefineMany(this, NumberOfGenerators, (register, index) =>
            {
                register
                    .WithFlag(0, out enable[index], name: "ENABLE")
                    .WithFlag(1, FieldMode.Read, valueProviderCallback: _ => enable[index].Value, name: "RUNNING")
                    .WithReservedBits(2, 30)
                    .WithChangeCallback((_, __) => TicksChanged?.Invoke());
            }, stepInBytes: GeneratorStride);

            Registers.Proc0Cycles.DefineMany(this, NumberOfGenerators, (register, index) =>
            {
                register
                    .WithValueField(0, 9, out cycles[index], name: "CYCLES")
                    .WithReservedBits(9, 23)
                    .WithChangeCallback((_, __) => TicksChanged?.Invoke());
            }, stepInBytes: GeneratorStride);

            // Cycles left until the next tick. Nothing can make use of it at
            // the resolution the model runs at.
            Registers.Proc0Count.DefineMany(this, NumberOfGenerators, (register, index) =>
            {
                register
                    .WithValueField(0, 9, FieldMode.Read, name: "COUNT")
                    .WithReservedBits(9, 23);
            }, stepInBytes: GeneratorStride);
        }

        public override void Reset()
        {
            base.Reset();
            TicksChanged?.Invoke();
        }

        // Tick rate of a generator in Hz, 0 while it is stopped.
        public long GetFrequency(Generator generator)
        {
            var index = (int)generator;
            if(!enable[index].Value || cycles[index].Value == 0)
            {
                return 0;
            }
            return clocks.ReferenceFrequency / (long)cycles[index].Value;
        }

        // clk_sys in Hz, for the consumers that can count it instead of ticks.
        public long SystemFrequency => clocks.SystemFrequency;

        // Raised whenever a tick rate may have changed.
        public event Action TicksChanged;

        public enum Generator
        {
            Proc0,
            Proc1,
            Timer0,
            Timer1,
            Watchdog,
            RiscV,
        }

        private readonly RP2350_Clocks clocks;
        private readonly IFlagRegisterField[] enable = new IFlagRegisterField[NumberOfGenerators];
        private readonly IValueRegisterField[] cycles = new IValueRegisterField[NumberOfGenerators];

        private const uint NumberOfGenerators = 6;
        private const uint GeneratorStride = 0xC;

        private enum Registers : long
        {
            Proc0Control = 0x00,
            Proc0Cycles = 0x04,
            Proc0Count = 0x08,
        }
    }
}
