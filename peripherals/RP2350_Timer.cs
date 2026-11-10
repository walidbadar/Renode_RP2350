//
// Copyright (c) 2026 Muhammad Waleed Badar
// SPDX-License-Identifier: GPL-3.0-only
//
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using Antmicro.Renode.Core;
using Antmicro.Renode.Core.Structure.Registers;
using Antmicro.Renode.Peripherals.Miscellaneous;
using Antmicro.Renode.Time;

namespace Antmicro.Renode.Peripherals.Timers
{
    // TIMER0 and TIMER1: a 64-bit counter, nominally of microseconds, with
    // four alarms that match on its low half. Each alarm has its own
    // interrupt line, and the lines go to both cores.
    //
    // The counter advances on the ticks of its generator in the TICKS block,
    // or on clk_sys when SOURCE says so. Without a TICKS block to follow it
    // counts at 1 MHz.
    public class RP2350_Timer : RP2350_RegisterBlock, INumberedGPIOOutput
    {
        public RP2350_Timer(IMachine machine, RP2350_Ticks ticks = null, uint index = 0) : base(machine)
        {
            this.ticks = ticks;
            generator = index == 0 ? RP2350_Ticks.Generator.Timer0 : RP2350_Ticks.Generator.Timer1;

            var connections = new Dictionary<int, IGPIO>();
            for(var alarm = 0; alarm < NumberOfAlarms; alarm++)
            {
                connections[alarm] = new GPIO();
            }
            Connections = new ReadOnlyDictionary<int, IGPIO>(connections);

            // The low half of the count; the wrap handler carries into the
            // high half.
            counter = new LimitTimer(machine.ClockSource, DefaultFrequency, this, "counter", limit: 1UL << 32,
                direction: Direction.Ascending, enabled: false, workMode: WorkMode.Periodic, eventEnabled: true);
            counter.LimitReached += () => Interlocked.Increment(ref highCount);

            alarms = new LimitTimer[NumberOfAlarms];
            for(var alarm = 0; alarm < NumberOfAlarms; alarm++)
            {
                var number = alarm;
                alarms[alarm] = new LimitTimer(machine.ClockSource, DefaultFrequency, this, $"alarm{alarm}", limit: 1UL << 32,
                    direction: Direction.Descending, enabled: false, workMode: WorkMode.OneShot, eventEnabled: true);
                alarms[alarm].LimitReached += () => AlarmReached(number);
            }

            DefineRegisters();

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
            base.Reset();
            lock(locker)
            {
                lock(interruptLock)
                {
                    armed = 0;
                    rawInterrupts = 0;
                    enabledInterrupts = 0;
                    forcedInterrupts = 0;
                    UpdateInterrupts();
                }
                foreach(var alarm in alarms)
                {
                    alarm.Enabled = false;
                }
                counter.Enabled = false;
                counter.Value = 0;
                Interlocked.Exchange(ref highCount, 0);
                latchedHigh = 0;
                pendingLow = 0;
                UpdateRate();
            }
        }

        public IReadOnlyDictionary<int, IGPIO> Connections { get; }

        private void DefineRegisters()
        {
            // Writing the low half only stages it; the high half commits both.
            Registers.TimeHighWrite.Define(this)
                .WithValueField(0, 32, FieldMode.Write,
                    writeCallback: (_, value) => SetTime((value << 32) | pendingLow), name: "TIMEHW");
            Registers.TimeLowWrite.Define(this)
                .WithValueField(0, 32, FieldMode.Write,
                    writeCallback: (_, value) => pendingLow = (uint)value, name: "TIMELW");

            // Reading the low half latches the high half, so that the two
            // reads together give one consistent 64-bit time.
            Registers.TimeHighRead.Define(this)
                .WithValueField(0, 32, FieldMode.Read, valueProviderCallback: _ => latchedHigh, name: "TIMEHR");
            Registers.TimeLowRead.Define(this)
                .WithValueField(0, 32, FieldMode.Read, valueProviderCallback: _ =>
                {
                    var time = GetTime();
                    latchedHigh = (uint)(time >> 32);
                    return (uint)time;
                }, name: "TIMELR");

            Registers.Alarm0.DefineMany(this, NumberOfAlarms, (register, alarm) =>
            {
                register.WithValueField(0, 32, valueProviderCallback: _ => targets[alarm],
                    writeCallback: (_, value) =>
                    {
                        // Writing the time to fire at arms the alarm.
                        targets[alarm] = (uint)value;
                        lock(interruptLock)
                        {
                            armed |= 1u << alarm;
                        }
                        Schedule(alarm);
                    }, name: "ALARM");
            });

            Registers.Armed.Define(this)
                .WithValueField(0, NumberOfAlarms, valueProviderCallback: _ => armed,
                    writeCallback: (_, value) =>
                    {
                        // Writing 1 disarms.
                        lock(interruptLock)
                        {
                            armed &= ~(uint)value;
                        }
                        for(var alarm = 0; alarm < NumberOfAlarms; alarm++)
                        {
                            if((value & (1UL << alarm)) != 0)
                            {
                                alarms[alarm].Enabled = false;
                            }
                        }
                    }, name: "ARMED")
                .WithReservedBits(NumberOfAlarms, 32 - NumberOfAlarms);
            MarkWriteOneToClear((long)Registers.Armed);

            Registers.TimeRawHigh.Define(this)
                .WithValueField(0, 32, FieldMode.Read, valueProviderCallback: _ => GetTime() >> 32, name: "TIMERAWH");
            Registers.TimeRawLow.Define(this)
                .WithValueField(0, 32, FieldMode.Read, valueProviderCallback: _ => (uint)GetTime(), name: "TIMERAWL");

            // There is no debugger to pause for.
            Registers.DebugPause.Define(this, 0x7)
                .WithValueField(0, 3, name: "DBGPAUSE")
                .WithReservedBits(3, 29);

            Registers.Pause.Define(this)
                .WithFlag(0, out paused, name: "PAUSE")
                .WithReservedBits(1, 31)
                .WithChangeCallback((_, __) => UpdateRate());

            Registers.Locked.Define(this)
                .WithFlag(0, name: "LOCKED")
                .WithReservedBits(1, 31);

            Registers.Source.Define(this)
                .WithFlag(0, out countSystemClock, name: "CLK_SYS")
                .WithReservedBits(1, 31)
                .WithChangeCallback((_, __) => UpdateRate());

            Registers.RawInterrupts.Define(this)
                .WithValueField(0, NumberOfAlarms, valueProviderCallback: _ => rawInterrupts,
                    writeCallback: (_, value) =>
                    {
                        lock(interruptLock)
                        {
                            rawInterrupts &= ~(uint)value;
                            UpdateInterrupts();
                        }
                    }, name: "INTR")
                .WithReservedBits(NumberOfAlarms, 32 - NumberOfAlarms);
            MarkWriteOneToClear((long)Registers.RawInterrupts);

            Registers.InterruptEnable.Define(this)
                .WithValueField(0, NumberOfAlarms, valueProviderCallback: _ => enabledInterrupts,
                    writeCallback: (_, value) =>
                    {
                        lock(interruptLock)
                        {
                            enabledInterrupts = (uint)value;
                            UpdateInterrupts();
                        }
                    }, name: "INTE")
                .WithReservedBits(NumberOfAlarms, 32 - NumberOfAlarms);

            Registers.InterruptForce.Define(this)
                .WithValueField(0, NumberOfAlarms, valueProviderCallback: _ => forcedInterrupts,
                    writeCallback: (_, value) =>
                    {
                        lock(interruptLock)
                        {
                            forcedInterrupts = (uint)value;
                            UpdateInterrupts();
                        }
                    }, name: "INTF")
                .WithReservedBits(NumberOfAlarms, 32 - NumberOfAlarms);

            Registers.InterruptStatus.Define(this)
                .WithValueField(0, NumberOfAlarms, FieldMode.Read,
                    valueProviderCallback: _ => (rawInterrupts | forcedInterrupts) & enabledInterrupts, name: "INTS")
                .WithReservedBits(NumberOfAlarms, 32 - NumberOfAlarms);
        }

        private ulong GetTime()
        {
            // The carry may land between the two reads; go again if it did.
            while(true)
            {
                var high = Interlocked.Read(ref highCount);
                var low = counter.Value;
                if(high == Interlocked.Read(ref highCount))
                {
                    return ((ulong)high << 32) | low;
                }
            }
        }

        private void SetTime(ulong time)
        {
            counter.Value = (uint)time;
            Interlocked.Exchange(ref highCount, (long)(time >> 32));
            for(var alarm = 0; alarm < NumberOfAlarms; alarm++)
            {
                Schedule(alarm);
            }
        }

        // Tell the alarm how far away its match is. An alarm fires when the
        // low half of the time equals its target, so a target that has just
        // gone by is a full wrap of the counter away.
        private void Schedule(int alarm)
        {
            if((armed & (1u << alarm)) == 0)
            {
                alarms[alarm].Enabled = false;
                return;
            }

            var distance = targets[alarm] - (uint)GetTime();
            if(distance == 0)
            {
                alarms[alarm].Enabled = false;
                AlarmReached(alarm);
                return;
            }
            alarms[alarm].Limit = distance;
            alarms[alarm].ResetValue();
            alarms[alarm].Enabled = running;
        }

        // Runs with the clock source locked, so it must not wait for the
        // register lock: a core holding that may be waiting for the clock.
        private void AlarmReached(int alarm)
        {
            lock(interruptLock)
            {
                var bit = 1u << alarm;
                if((armed & bit) == 0)
                {
                    return;
                }
                armed &= ~bit;
                rawInterrupts |= bit;
                UpdateInterrupts();
            }
        }

        private void UpdateInterrupts()
        {
            var status = (rawInterrupts | forcedInterrupts) & enabledInterrupts;
            for(var alarm = 0; alarm < NumberOfAlarms; alarm++)
            {
                Connections[alarm].Set((status & (1u << alarm)) != 0);
            }
        }

        // Follow the selected count source. With nothing to count, the
        // counter and the alarms stand still.
        private void UpdateRate()
        {
            long frequency;
            if(ticks == null)
            {
                frequency = DefaultFrequency;
            }
            else if(countSystemClock.Value)
            {
                frequency = ticks.SystemFrequency;
            }
            else
            {
                frequency = ticks.GetFrequency(generator);
            }

            running = frequency > 0 && !paused.Value;
            if(frequency > 0)
            {
                counter.Frequency = frequency;
                foreach(var alarm in alarms)
                {
                    alarm.Frequency = frequency;
                }
            }
            counter.Enabled = running;
            for(var alarm = 0; alarm < NumberOfAlarms; alarm++)
            {
                alarms[alarm].Enabled = running && (armed & (1u << alarm)) != 0;
            }
        }

        private bool running;
        private long highCount;
        private uint latchedHigh;
        private uint pendingLow;

        // Guarded by interruptLock, which is never held across a call into
        // the clock source.
        private uint armed;
        private uint rawInterrupts;
        private uint enabledInterrupts;
        private uint forcedInterrupts;

        private IFlagRegisterField paused;
        private IFlagRegisterField countSystemClock;

        private readonly RP2350_Ticks ticks;
        private readonly RP2350_Ticks.Generator generator;
        private readonly LimitTimer counter;
        private readonly LimitTimer[] alarms;
        private readonly uint[] targets = new uint[NumberOfAlarms];
        private readonly object interruptLock = new object();

        private const int NumberOfAlarms = 4;
        private const long DefaultFrequency = 1000000;

        private enum Registers : long
        {
            TimeHighWrite = 0x00,
            TimeLowWrite = 0x04,
            TimeHighRead = 0x08,
            TimeLowRead = 0x0C,
            Alarm0 = 0x10,
            Armed = 0x20,
            TimeRawHigh = 0x24,
            TimeRawLow = 0x28,
            DebugPause = 0x2C,
            Pause = 0x30,
            Locked = 0x34,
            Source = 0x38,
            RawInterrupts = 0x3C,
            InterruptEnable = 0x40,
            InterruptForce = 0x44,
            InterruptStatus = 0x48,
        }
    }
}
