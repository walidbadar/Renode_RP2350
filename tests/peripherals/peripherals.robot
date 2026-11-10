# Copyright (c) 2026 Muhammad Waleed Badar
# SPDX-License-Identifier: Apache-2.0
#
# The peripheral models one register at a time, from the Monitor. No image
# is loaded. Where time has to pass the machine runs with both cores resting
# in the boot ROM. An access from the Monitor counts as one from core 0.
#
# Twister runs this and passes ${ELF}, the image it built. The image is not
# loaded, but the boot ROM stand-in is found next to it.

*** Variables ***
${PLATFORM}                   ${CURDIR}/../../scripts/rp2350.resc
${BOARD}                      ${CURDIR}/../../platforms/rpi_pico2.repl

# Offsets of the aliases that every block but the SIO has.
${XOR}                        0x1000
${SET}                        0x2000
${CLR}                        0x3000

${SYSINFO}                    0x40000000
${CLOCKS}                     0x40010000
${PSM}                        0x40018000
${RESETS}                     0x40020000
${IO_BANK0}                   0x40028000
${PADS_BANK0}                 0x40038000
${XOSC}                       0x40048000
${PLL_SYS}                    0x40050000
${UART0}                      0x40070000
${TIMER0}                     0x400B0000
${WATCHDOG}                   0x400D8000
${BOOTRAM}                    0x400E0000
${TICKS}                      0x40108000
${TBMAN}                      0x40160000
${SIO}                        0xD0000000

*** Keywords ***
Create Machine
    Execute Command           $elf=${ELF}
    Execute Script            ${PLATFORM}

Write Register
    [Arguments]               ${base}  ${offset}  ${value}
    ${address}=               Evaluate  hex(${base} + ${offset})
    Execute Command           sysbus WriteDoubleWord ${address} ${value}

Read Register
    [Arguments]               ${base}  ${offset}
    ${address}=               Evaluate  hex(${base} + ${offset})
    ${value}=                 Execute Command  sysbus ReadDoubleWord ${address}
    ${number}=                Convert To Integer  ${value.strip()}  16
    RETURN                    ${number}

Register Should Be
    [Arguments]               ${base}  ${offset}  ${expected}
    ${value}=                 Read Register  ${base}  ${offset}
    Should Be Equal As Integers  ${value}  ${expected}

Run For Microseconds
    [Arguments]               ${microseconds}
    ${seconds}=               Evaluate  "{:.6f}".format(${microseconds} / 1000000)
    Execute Command           emulation RunFor "${seconds}"

LED Should Be
    [Arguments]               ${expected}
    ${state}=                 Execute Command  io_bank0.led State
    Should Contain            ${state}  ${expected}

Line Should Be
    [Arguments]               ${line}  ${expected}
    ${state}=                 Execute Command  ${line} IsSet
    Should Contain            ${state}  ${expected}

# The crystal at 12 MHz as the reference clock, and a 1 MHz tick from it for
# TIMER0 and the watchdog.
Start Microsecond Ticks
    Write Register            ${XOSC}  0x00  0x00FABAA0
    Write Register            ${CLOCKS}  0x30  0x2
    Write Register            ${TICKS}  0x1C  12
    Write Register            ${TICKS}  0x18  0x1
    Write Register            ${TICKS}  0x34  12
    Write Register            ${TICKS}  0x30  0x1

*** Test Cases ***
Should Identify As RP2350A
    Create Machine
    # Revision A2, part 0x0004, Raspberry Pi's JEDEC code, stop bit.
    Register Should Be        ${SYSINFO}  0x00  0x20004927
    Register Should Be        ${SYSINFO}  0x04  0x1
    Register Should Be        ${SYSINFO}  0x08  0x2
    Register Should Be        ${TBMAN}  0x00  0x1

Should Decode Atomic Aliases
    [Documentation]           The same register through its plain, XOR, set and clear addresses.
    Create Machine
    Write Register            ${PSM}  0x8  0x0F
    Write Register            ${PSM}  ${SET} + 0x8  0xF0
    Register Should Be        ${PSM}  0x8  0xFF
    Write Register            ${PSM}  ${CLR} + 0x8  0x0F
    Register Should Be        ${PSM}  0x8  0xF0
    Write Register            ${PSM}  ${XOR} + 0x8  0xFF
    Register Should Be        ${PSM}  0x8  0x0F
    # Reading through an alias reads the register.
    Register Should Be        ${PSM}  ${SET} + 0x8  0x0F

Should Decode Atomic Aliases Of The UART
    [Documentation]           The UART model is Renode's; the bridge in front of it adds the aliases.
    Create Machine
    Write Register            ${UART0}  0x30  0x0
    Write Register            ${UART0}  ${SET} + 0x30  0x301
    Register Should Be        ${UART0}  0x30  0x301
    Write Register            ${UART0}  ${CLR} + 0x30  0x001
    Register Should Be        ${UART0}  0x30  0x300
    Write Register            ${UART0}  ${XOR} + 0x30  0x101
    Register Should Be        ${UART0}  0x30  0x201

Should Hold And Release Blocks In Reset
    Create Machine
    # Everything starts in reset; nothing is done coming out of it.
    Register Should Be        ${RESETS}  0x0  0x1FFFFFFF
    Register Should Be        ${RESETS}  0x8  0x0
    # TIMER0 and UART0.
    Write Register            ${RESETS}  ${CLR} + 0x0  0x04800000
    Register Should Be        ${RESETS}  0x0  0x1B7FFFFF
    Register Should Be        ${RESETS}  0x8  0x04800000

    # Putting a block back into reset resets its model.
    Write Register            ${TIMER0}  0x40  0xF
    Register Should Be        ${TIMER0}  0x40  0xF
    Write Register            ${RESETS}  ${SET} + 0x0  0x00800000
    Register Should Be        ${TIMER0}  0x40  0x0

Should Follow The Clock Tree
    [Documentation]           The frequency counter measures what the model believes each clock runs at.
    Create Machine
    # Out of reset clk_sys runs from the ring oscillator, nominally 11 MHz.
    Write Register            ${CLOCKS}  0xA0  0x9
    Register Should Be        ${CLOCKS}  0xA4  0x11
    ${result}=                Read Register  ${CLOCKS}  0xA8
    Should Be Equal As Integers  ${result >> 5}  11000
    Register Should Be        ${CLOCKS}  0x44  0x2

    # A PLL cannot lock without its reference.
    Write Register            ${PLL_SYS}  0x08  125
    Write Register            ${PLL_SYS}  ${CLR} + 0x04  0x21
    Register Should Be        ${PLL_SYS}  0x00  0x00000001

    # Crystal on: stable at once, and the PLL locks.
    Register Should Be        ${XOSC}  0x04  0x0
    Write Register            ${XOSC}  0x00  0x00FABAA0
    Register Should Be        ${XOSC}  0x04  0x80001000
    Register Should Be        ${PLL_SYS}  0x00  0x80000001

    # 12 MHz x 125 / 5 / 2 = 150 MHz, once the post dividers are powered.
    Write Register            ${PLL_SYS}  0x0C  0x00052000
    Write Register            ${PLL_SYS}  ${CLR} + 0x04  0x08
    Write Register            ${CLOCKS}  0xA0  0x1
    ${result}=                Read Register  ${CLOCKS}  0xA8
    Should Be Equal As Integers  ${result >> 5}  150000

    # clk_ref from the crystal, clk_sys from the PLL.
    Write Register            ${CLOCKS}  0x30  0x2
    Register Should Be        ${CLOCKS}  0x38  0x4
    Write Register            ${CLOCKS}  0x3C  0x1
    Register Should Be        ${CLOCKS}  0x44  0x2
    Write Register            ${CLOCKS}  0xA0  0x9
    ${result}=                Read Register  ${CLOCKS}  0xA8
    Should Be Equal As Integers  ${result >> 5}  150000

    # SysTick counts cycles of clk_sys, on both cores.
    ${systick}=               Execute Command  nvic0 Frequency
    Should Be Equal As Integers  ${systick}  150000000
    ${systick}=               Execute Command  nvic1 Frequency
    Should Be Equal As Integers  ${systick}  150000000

    # Halving clk_sys halves it: the divider is 16.16 fixed point.
    Write Register            ${CLOCKS}  0x40  0x00020000
    ${systick}=               Execute Command  nvic0 Frequency
    Should Be Equal As Integers  ${systick}  75000000

    # clk_peri is gated, and says whether it runs.
    Write Register            ${CLOCKS}  0xA0  0xA
    ${result}=                Read Register  ${CLOCKS}  0xA8
    Should Be Equal As Integers  ${result >> 5}  0
    Write Register            ${CLOCKS}  0x48  0x800
    Register Should Be        ${CLOCKS}  0x48  0x10000800
    ${result}=                Read Register  ${CLOCKS}  0xA8
    Should Be Equal As Integers  ${result >> 5}  75000

Should Count Ticks In The Timer
    [Documentation]           The timer stands still until its tick generator runs.
    Create Machine
    Run For Microseconds      1000
    Register Should Be        ${TIMER0}  0x28  0

    Start Microsecond Ticks
    Register Should Be        ${TICKS}  0x18  0x3
    Run For Microseconds      1000
    Register Should Be        ${TIMER0}  0x28  1000

    # Paused, then running again.
    Write Register            ${TIMER0}  0x30  0x1
    Run For Microseconds      1000
    Register Should Be        ${TIMER0}  0x28  1000
    Write Register            ${TIMER0}  0x30  0x0
    Run For Microseconds      500
    Register Should Be        ${TIMER0}  0x28  1500

    # Twice the cycles per tick, half the rate.
    Write Register            ${TICKS}  0x1C  24
    Run For Microseconds      1000
    Register Should Be        ${TIMER0}  0x28  2000

    # Counting clk_sys instead: 11 MHz from the ring oscillator.
    Write Register            ${TIMER0}  0x38  0x1
    Run For Microseconds      1000
    Register Should Be        ${TIMER0}  0x28  13000

Should Set And Latch The Time
    Create Machine
    Start Microsecond Ticks
    # The low half is staged; writing the high half commits both.
    Write Register            ${TIMER0}  0x04  0xFFFFFF00
    Register Should Be        ${TIMER0}  0x28  0
    Write Register            ${TIMER0}  0x00  0x7
    Register Should Be        ${TIMER0}  0x28  0xFFFFFF00
    Register Should Be        ${TIMER0}  0x24  0x7

    # Across the carry: reading the low half latches the high half.
    Run For Microseconds      1000
    Register Should Be        ${TIMER0}  0x0C  0x000002E8
    Register Should Be        ${TIMER0}  0x08  0x8
    Register Should Be        ${TIMER0}  0x24  0x8

Should Fire Alarms
    Create Machine
    Start Microsecond Ticks
    Write Register            ${TIMER0}  0x40  0x5
    Run For Microseconds      100

    # Alarm 0 in 500 us, alarm 2 in 2000 us; writing the target arms.
    Write Register            ${TIMER0}  0x10  600
    Write Register            ${TIMER0}  0x18  2100
    Register Should Be        ${TIMER0}  0x20  0x5
    Run For Microseconds      400
    Register Should Be        ${TIMER0}  0x3C  0x0
    Run For Microseconds      200

    # Fired: disarmed, raw and masked status set.
    Register Should Be        ${TIMER0}  0x20  0x4
    Register Should Be        ${TIMER0}  0x3C  0x1
    Register Should Be        ${TIMER0}  0x48  0x1

    # Acknowledged by writing 1; the alias for clearing bits does the same.
    Write Register            ${TIMER0}  0x3C  0x1
    Register Should Be        ${TIMER0}  0x3C  0x0

    # Writing 1 to ARMED disarms: alarm 2 never fires.
    Write Register            ${TIMER0}  0x20  0x4
    Register Should Be        ${TIMER0}  0x20  0x0
    Run For Microseconds      2000
    Register Should Be        ${TIMER0}  0x3C  0x0

    # An interrupt can be forced, and only counts if enabled.
    Write Register            ${TIMER0}  0x44  0x6
    Register Should Be        ${TIMER0}  0x48  0x4
    Write Register            ${TIMER0}  ${CLR} + 0x44  0x4
    Register Should Be        ${TIMER0}  0x48  0x0

Should Tell Core 0 It Is Core 0
    Create Machine
    Register Should Be        ${SIO}  0x000  0
    # Compilers read the core number with a byte load.
    ${cpuid}=                 Execute Command  sysbus ReadByte 0xD0000000
    Should Be Equal As Numbers  ${cpuid}  0

Should Hold Four Words In The Mailbox
    [Documentation]           Core 0's side of the mailbox to core 1, with nobody reading.
    Create Machine
    Register Should Be        ${SIO}  0x050  0x2
    FOR  ${word}  IN RANGE  4
        Register Should Be    ${SIO}  0x050  0x2
        Write Register        ${SIO}  0x054  ${word}
    END
    # Full: not ready, and the next write is dropped and flagged.
    Register Should Be        ${SIO}  0x050  0x0
    Write Register            ${SIO}  0x054  0x99
    Register Should Be        ${SIO}  0x050  0x4
    Line Should Be            sio FifoIRQ0  True
    # Drivers clear the flags with all ones.
    Write Register            ${SIO}  0x050  0xFF
    Register Should Be        ${SIO}  0x050  0x0
    Line Should Be            sio FifoIRQ0  False

    # Core 1 has four words waiting and its interrupt asserted.
    Line Should Be            sio FifoIRQ1  True

    # Nothing has been sent to core 0: reading is flagged.
    Register Should Be        ${SIO}  0x058  0x0
    Register Should Be        ${SIO}  0x050  0x8
    Write Register            ${SIO}  0x050  0x8
    Register Should Be        ${SIO}  0x050  0x0

Should Claim And Release Spinlocks
    Create Machine
    Register Should Be        ${SIO}  0x05C  0x0
    # Reading claims and returns the lock's bit; a held lock reads as 0.
    Register Should Be        ${SIO}  0x114  0x20
    Register Should Be        ${SIO}  0x114  0x0
    Register Should Be        ${SIO}  0x17C  0x80000000
    Register Should Be        ${SIO}  0x05C  0x80000020
    # Any write releases.
    Write Register            ${SIO}  0x114  0x0
    Register Should Be        ${SIO}  0x05C  0x80000000
    Register Should Be        ${SIO}  0x114  0x20

Should Ring Doorbells
    Create Machine
    # To the other core.
    Write Register            ${SIO}  0x180  0x05
    Register Should Be        ${SIO}  0x180  0x05
    Line Should Be            sio DoorbellIRQ1  True
    Line Should Be            sio DoorbellIRQ0  False
    Write Register            ${SIO}  0x184  0x01
    Register Should Be        ${SIO}  0x184  0x04
    Write Register            ${SIO}  0x184  0x04
    Line Should Be            sio DoorbellIRQ1  False

    # To this core, and acknowledged by it.
    Write Register            ${SIO}  0x188  0x82
    Register Should Be        ${SIO}  0x18C  0x82
    Line Should Be            sio DoorbellIRQ0  True
    Write Register            ${SIO}  0x18C  0x02
    Register Should Be        ${SIO}  0x18C  0x80
    Line Should Be            sio DoorbellIRQ0  True
    Write Register            ${SIO}  0x18C  0x80
    Line Should Be            sio DoorbellIRQ0  False

Should Drive Pins From The SIO
    [Documentation]           Output goes SIO, function select, pad; the pad passes nothing on until its isolation is removed.
    # On the Pico 2 the LED is on GPIO25.
    Execute Command           $platform=@${BOARD}
    Create Machine
    # Set, clear and toggle registers of the outputs and the output enables.
    Write Register            ${SIO}  0x010  0x0000000F
    Write Register            ${SIO}  0x018  0x000000F0
    Write Register            ${SIO}  0x020  0x00000003
    Write Register            ${SIO}  0x028  0x00000101
    Register Should Be        ${SIO}  0x010  0x000001FD
    Write Register            ${SIO}  0x014  0x0000FFFF
    Register Should Be        ${SIO}  0x014  0x0000FFFF
    Write Register            ${SIO}  0x010  0x0
    Write Register            ${SIO}  0x014  0x0

    # GPIO25 as an output, high. Its function is still "none".
    Write Register            ${SIO}  0x038  0x02000000
    Write Register            ${SIO}  0x018  0x02000000
    Register Should Be        ${SIO}  0x030  0x02000000
    Register Should Be        ${IO_BANK0}  0x0C8  0x0

    # Function SIO, but the pad is still isolated.
    Write Register            ${IO_BANK0}  0x0CC  0x5
    Register Should Be        ${IO_BANK0}  0x0C8  0x0
    LED Should Be             False

    # Isolation off: output enable and level reach the pad.
    Write Register            ${PADS_BANK0}  ${CLR} + 0x68  0x100
    Register Should Be        ${IO_BANK0}  0x0C8  0x00002200
    LED Should Be             True

    # The pad's input buffer is off, so the pin reads back low.
    Register Should Be        ${SIO}  0x004  0x0
    Write Register            ${PADS_BANK0}  ${SET} + 0x68  0x40
    Register Should Be        ${SIO}  0x004  0x02000000
    Register Should Be        ${IO_BANK0}  0x0C8  0x04022200

    # Output override: inverted.
    Write Register            ${IO_BANK0}  0x0CC  0x1005
    LED Should Be             False
    Write Register            ${IO_BANK0}  0x0CC  0x5

    # Through the toggle register.
    Write Register            ${SIO}  0x028  0x02000000
    LED Should Be             False

Should Raise Pin Interrupts Per Core
    [Documentation]           One set of raw events, separate enables and lines for each core.
    Create Machine
    # GPIO5 as an input.
    Write Register            ${PADS_BANK0}  0x18  0x56
    Register Should Be        ${SIO}  0x004  0x0
    Register Should Be        ${IO_BANK0}  0x230  0x11111111

    # Core 0 wants its rising edge, core 1 its high level.
    Write Register            ${IO_BANK0}  0x248  0x00800000
    Write Register            ${IO_BANK0}  0x290  0x00200000
    Line Should Be            io_bank0 Proc0IRQ  False
    Line Should Be            io_bank0 Proc1IRQ  False

    Execute Command           io_bank0 OnGPIO 5 true
    Register Should Be        ${SIO}  0x004  0x20
    Register Should Be        ${IO_BANK0}  0x230  0x11A11111
    Register Should Be        ${IO_BANK0}  0x278  0x00800000
    Register Should Be        ${IO_BANK0}  0x2C0  0x00200000
    Register Should Be        ${IO_BANK0}  0x200  0x20
    Register Should Be        ${IO_BANK0}  0x210  0x20
    Line Should Be            io_bank0 Proc0IRQ  True
    Line Should Be            io_bank0 Proc1IRQ  True

    # The edge is acknowledged by writing it; the level cannot be.
    Write Register            ${IO_BANK0}  0x230  0x00F00000
    Register Should Be        ${IO_BANK0}  0x230  0x11211111
    Line Should Be            io_bank0 Proc0IRQ  False
    Line Should Be            io_bank0 Proc1IRQ  True

    # Low again: the level goes away, a falling edge is recorded.
    Execute Command           io_bank0 OnGPIO 5 false
    Register Should Be        ${IO_BANK0}  0x230  0x11511111
    Line Should Be            io_bank0 Proc1IRQ  False

    # Forced for core 1 only.
    Write Register            ${IO_BANK0}  0x2A8  0x00200000
    Line Should Be            io_bank0 Proc1IRQ  True
    Line Should Be            io_bank0 Proc0IRQ  False
    Write Register            ${IO_BANK0}  0x2A8  0x0

    # Input override: the peripherals see the pin inverted, interrupts do not.
    Write Register            ${IO_BANK0}  0x02C  0x0001001F
    Register Should Be        ${SIO}  0x004  0x20
    Register Should Be        ${IO_BANK0}  0x230  0x11511111

Should Keep Bootrom Locks And Memory
    Create Machine
    # RAM, by word and by byte.
    Write Register            ${BOOTRAM}  0x3FC  0x11223344
    Register Should Be        ${BOOTRAM}  0x3FC  0x11223344
    Execute Command           sysbus WriteByte 0x400E03FD 0xAA
    Register Should Be        ${BOOTRAM}  0x3FC  0x1122AA44

    # Bits that can be set but not cleared.
    Write Register            ${BOOTRAM}  0x800  0x0F
    Write Register            ${BOOTRAM}  0x800  0xF0
    Register Should Be        ${BOOTRAM}  0x800  0xFF

    # Eight locks like the SIO's; the status shows the free ones.
    Register Should Be        ${BOOTRAM}  0x808  0xFF
    Register Should Be        ${BOOTRAM}  0x828  0x80
    Register Should Be        ${BOOTRAM}  0x828  0x00
    Register Should Be        ${BOOTRAM}  0x808  0x7F
    Write Register            ${BOOTRAM}  0x828  0x0
    Register Should Be        ${BOOTRAM}  0x808  0xFF

Should Report Power Stages
    Create Machine
    Register Should Be        ${PSM}  0xC  0x01FFFFFF
    # Core 1 held: not done, and halted.
    Write Register            ${PSM}  ${SET} + 0x4  0x01000000
    Register Should Be        ${PSM}  0x4  0x01000000
    Register Should Be        ${PSM}  0xC  0x00FFFFFF
    ${halted}=                Execute Command  cpu1 IsHalted
    Should Contain            ${halted}  True
    Write Register            ${PSM}  ${CLR} + 0x4  0x01000000
    Register Should Be        ${PSM}  0xC  0x01FFFFFF
    ${halted}=                Execute Command  cpu1 IsHalted
    Should Contain            ${halted}  False

Should Keep Scratch Registers Across A Watchdog Reset
    Create Machine
    Start Microsecond Ticks
    Write Register            ${WATCHDOG}  0x0C  0xCAFE0001
    Write Register            ${WATCHDOG}  0x28  0xCAFE0008
    Register Should Be        ${WATCHDOG}  0x00  0x07000000

    # With nothing selected in the PSM the watchdog only notes its reason.
    Write Register            ${WATCHDOG}  0x04  1000
    Write Register            ${WATCHDOG}  ${SET} + 0x00  0x40000000
    Run For Microseconds      1500
    Register Should Be        ${WATCHDOG}  0x08  0x1
    Register Should Be        ${TIMER0}  0x28  1500

    # With the processors selected it resets the machine.
    Write Register            ${PSM}  0x8  0x01FFFFF3
    Write Register            ${WATCHDOG}  0x04  1000
    Run For Microseconds      1500
    Register Should Be        ${WATCHDOG}  0x08  0x1
    Register Should Be        ${WATCHDOG}  0x00  0x07000000
    Register Should Be        ${WATCHDOG}  0x0C  0xCAFE0001
    Register Should Be        ${WATCHDOG}  0x28  0xCAFE0008
    # Everything else started over.
    Register Should Be        ${PSM}  0x8  0x0
    Register Should Be        ${TICKS}  0x18  0x0

    # A forced reset says so.
    Write Register            ${PSM}  0x8  0x01FFFFF3
    Write Register            ${WATCHDOG}  0x00  0x80000000
    Run For Microseconds      100
    Register Should Be        ${WATCHDOG}  0x08  0x2
    Register Should Be        ${WATCHDOG}  0x0C  0xCAFE0001
