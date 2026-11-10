//
// Copyright (c) 2026 Muhammad Waleed Badar
// SPDX-License-Identifier: Apache-2.0
//
using Antmicro.Renode.Core;
using Antmicro.Renode.Core.Structure.Registers;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Miscellaneous;
using Antmicro.Renode.Time;

namespace Antmicro.Renode.Peripherals.Timers
{
    // WATCHDOG: a down counter that resets the chip when it runs out, plus
    // eight scratch registers that survive that reset.
    //
    // What a watchdog reset takes down is chosen in PSM.WDSEL. The model
    // knows two outcomes: nothing selected, where the watchdog only records
    // its reason, and anything selected, where the whole machine is reset
    // with the scratch registers and the reason kept.
    public class RP2350_Watchdog : RP2350_RegisterBlock
    {
        public RP2350_Watchdog(IMachine machine, RP2350_Ticks ticks = null, RP2350_PSM psm = null) : base(machine)
        {
            this.ticks = ticks;
            this.psm = psm;

            counter = new LimitTimer(machine.ClockSource, DefaultFrequency, this, "counter", limit: MaximumLoad,
                direction: Direction.Descending, enabled: false, workMode: WorkMode.OneShot, eventEnabled: true);
            counter.LimitReached += () => Fire(ReasonTimer);

            Registers.Control.Define(this, 0x07000000)
                .WithValueField(0, 24, FieldMode.Read, valueProviderCallback: _ => enable.Value ? counter.Value : 0, name: "TIME")
                .WithValueField(24, 3, name: "PAUSE")
                .WithReservedBits(27, 3)
                .WithFlag(30, out enable, name: "ENABLE")
                .WithFlag(31, FieldMode.Write, writeCallback: (_, value) =>
                {
                    if(value)
                    {
                        Fire(ReasonForce);
                    }
                }, name: "TRIGGER")
                .WithChangeCallback((_, __) => UpdateRate());

            Registers.Load.Define(this)
                .WithValueField(0, 24, FieldMode.Write, writeCallback: (_, value) =>
                {
                    // Loading 0 would never count; the shortest wait is a tick.
                    counter.Limit = value == 0 ? 1 : value;
                    counter.ResetValue();
                    UpdateRate();
                }, name: "LOAD")
                .WithReservedBits(24, 8);

            Registers.Reason.Define(this)
                .WithValueField(0, 2, FieldMode.Read, valueProviderCallback: _ => reason, name: "REASON")
                .WithReservedBits(2, 30);

            Registers.Scratch0.DefineMany(this, NumberOfScratchRegisters, (register, index) =>
            {
                register.WithValueField(0, 32, valueProviderCallback: _ => scratch[index],
                    writeCallback: (_, value) => scratch[index] = (uint)value, name: "SCRATCH");
            });

            if(ticks != null)
            {
                ticks.TicksChanged += () =>
                {
                    lock(locker)
                    {
                        UpdateRate();
                    }
                };
            }
            Reset();
        }

        public override void Reset()
        {
            ResetCounter();
            reason = 0;
            for(var index = 0; index < NumberOfScratchRegisters; index++)
            {
                scratch[index] = 0;
            }
        }

        // What a watchdog reset leaves alone is exactly what is not here.
        private void ResetCounter()
        {
            base.Reset();
            lock(locker)
            {
                counter.Enabled = false;
                counter.Limit = MaximumLoad;
                counter.ResetValue();
                UpdateRate();
            }
        }

        // Runs with the clock source locked when the counter runs out, so it
        // must not wait for the register lock.
        private void Fire(uint why)
        {
            if(psm != null && psm.WatchdogSelection == 0)
            {
                this.Log(LogLevel.Warning, "The watchdog fired, but PSM.WDSEL selects nothing to reset");
                reason = why;
                return;
            }

            this.Log(LogLevel.Info, "Watchdog reset ({0})", why == ReasonForce ? "forced" : "timed out");
            machine.RequestResetInSafeState(() =>
            {
                ResetCounter();
                reason = why;
            }, new IPeripheral[] { this });
        }

        private void UpdateRate()
        {
            var frequency = ticks == null ? DefaultFrequency : ticks.GetFrequency(RP2350_Ticks.Generator.Watchdog);
            if(frequency > 0)
            {
                counter.Frequency = frequency;
            }
            counter.Enabled = frequency > 0 && enable.Value;
        }

        private volatile uint reason;

        private IFlagRegisterField enable;

        private readonly RP2350_Ticks ticks;
        private readonly RP2350_PSM psm;
        private readonly LimitTimer counter;
        private readonly uint[] scratch = new uint[NumberOfScratchRegisters];

        private const int NumberOfScratchRegisters = 8;
        private const ulong MaximumLoad = 0xFFFFFF;
        private const long DefaultFrequency = 1000000;
        private const uint ReasonTimer = 0x1;
        private const uint ReasonForce = 0x2;

        private enum Registers : long
        {
            Control = 0x00,
            Load = 0x04,
            Reason = 0x08,
            Scratch0 = 0x0C,
        }
    }
}
