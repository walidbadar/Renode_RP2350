# Copyright (c) 2026 Muhammad Waleed Badar
# SPDX-License-Identifier: Apache-2.0
#
# The bare-metal sample in samples/multicore. Twister runs this and passes
# ${ELF}, the image it built; the sample and the boot ROM stand-in are built
# next to that image.

*** Variables ***
${PLATFORM}                   ${CURDIR}/../../scripts/rp2350.resc
${SCRIPT}                     ${CURDIR}/../../scripts/rpi_pico2.resc
${SAMPLE}                     ${ELF.replace('zephyr.elf', 'multicore.elf')}
${UART}                       sysbus.uart0
# FRCE_OFF in the PSM block, and the bit in it that holds core 1 in reset.
${PSM_FRCE_OFF}               0x40018004
${PSM_PROC1}                  0x01000000
# The mailbox as core 0 sees it.
${SIO_FIFO_ST}                0xD0000050
${SIO_FIFO_RD}                0xD0000058
# AIRCR of core 0, and the key and bit that request a system reset.
${AIRCR}                      0xE000ED0C
${SYSRESETREQ}                0x05FA0004

*** Keywords ***
Create Machine
    Execute Command           $elf=${SAMPLE}
    Execute Script            ${SCRIPT}
    Create Terminal Tester    ${UART}

Should Be In Boot ROM
    [Arguments]               ${pc}
    ${address}=               Convert To Integer  ${pc.strip()}  16
    Should Be True            ${address} < 0x8000

Wait For Both Cores
    Wait For Line On Uart     RP2350 dual-core sample
    Wait For Line On Uart     core 0: running, CPUID 0
    Wait For Line On Uart     core 1: running, CPUID 1

Wait For All Checks
    Wait For Both Cores
    Wait For Line On Uart     launch core 1 ............ ok
    Wait For Line On Uart     mailbox ping-pong ........ ok
    Wait For Line On Uart     mailbox flags ............ ok
    Wait For Line On Uart     spinlock ................. ok
    Wait For Line On Uart     doorbell ................. ok
    Wait For Line On Uart     per-core SysTick ......... ok
    Wait For Line On Uart     timer alarm on core 1 .... ok
    # Core 1 announces itself a second time after being reset.
    Wait For Line On Uart     core 1: running, CPUID 1
    Wait For Line On Uart     reset and relaunch ....... ok
    Wait For Line On Uart     all 8 checks passed

*** Test Cases ***
Should Park Both Cores In The Boot ROM Without An Image
    [Documentation]           With nothing to boot, core 0 stops in the ROM and core 1 waits there to be launched.
    Execute Command           $elf=${SAMPLE}
    Execute Script            ${PLATFORM}
    Execute Command           emulation RunFor "0.001"

    ${pc0}=                   Execute Command  cpu0 PC
    Should Be In Boot ROM     ${pc0}
    ${pc1}=                   Execute Command  cpu1 PC
    Should Be In Boot ROM     ${pc1}

    # Core 1 has announced itself to core 0 with a zero and found room to send.
    ${status}=                Execute Command  sysbus ReadDoubleWord ${SIO_FIFO_ST}
    Should Be Equal As Numbers  ${status}  0x3
    ${word}=                  Execute Command  sysbus ReadDoubleWord ${SIO_FIFO_RD}
    Should Be Equal As Numbers  ${word}  0x0
    ${status}=                Execute Command  sysbus ReadDoubleWord ${SIO_FIFO_ST}
    Should Be Equal As Numbers  ${status}  0x2

Should Pass All Dual-Core Checks
    [Documentation]           Launch, mailbox, spinlocks, doorbells, SysTick, alarms and reset of core 1.
    Create Machine
    Start Emulation
    Wait For All Checks

Should Pass All Dual-Core Checks With Serial Execution
    [Documentation]           The same with the cores taking turns, which makes a run repeatable.
    Execute Command           emulation SetGlobalSerialExecution true
    Create Machine
    Start Emulation
    Wait For All Checks

Should Start Over After A Machine Reset
    [Documentation]           A reset from outside brings both cores back to the ROM.
    Create Machine
    Start Emulation
    Wait For All Checks

    Execute Command           machine Reset
    Wait For All Checks

Should Start Over After A Reset Request From Software
    [Documentation]           SYSRESETREQ from a core resets the whole chip.
    Create Machine
    Start Emulation
    Wait For All Checks

    Execute Command           sysbus WriteDoubleWord ${AIRCR} ${SYSRESETREQ} cpu0
    Wait For All Checks

Should Release Core 1 When Reset While It Is Held
    [Documentation]           A machine reset ends a hold on core 1 that software left in place.
    Create Machine
    Start Emulation
    Wait For All Checks

    Execute Command           sysbus WriteDoubleWord ${PSM_FRCE_OFF} ${PSM_PROC1}
    ${halted}=                Execute Command  cpu1 IsHalted
    Should Contain            ${halted}  True

    Execute Command           machine Reset
    Wait For All Checks
    ${halted}=                Execute Command  cpu1 IsHalted
    Should Contain            ${halted}  False
