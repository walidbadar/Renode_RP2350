# Copyright (c) 2026 Muhammad Waleed Badar
# SPDX-License-Identifier: GPL-3.0-only
#
# Zephyr on the Pico 2: stock samples from the Zephyr tree, built unchanged,
# and the sample from this repository that puts core 1 to work. "make zephyr"
# builds them all. A test is skipped while its sample has not been built.

*** Settings ***
Library                       OperatingSystem

*** Variables ***
${SCRIPT}                     ${CURDIR}/../scripts/rpi_pico2.resc
${ZEPHYR_BUILD}               ${CURDIR}/../build/zephyr
${UART}                       sysbus.uart0
${LED}                        sysbus.io_bank0.led
${PROMPT}                     uart:~$

*** Keywords ***
Run Zephyr Sample
    [Arguments]               ${sample}
    ${elf}=                   Set Variable  ${ZEPHYR_BUILD}/${sample}/zephyr/zephyr.elf
    ${built}=                 Run Keyword And Return Status  File Should Exist  ${elf}
    Skip If                   not ${built}  ${sample} is not built; run "make zephyr" with ZEPHYR_BASE set

    Execute Command           $elf=@${elf}
    Execute Script            ${SCRIPT}
    Create Terminal Tester    ${UART}
    Start Emulation

Core 1 Should Wait In The Boot ROM
    ${pc}=                    Execute Command  cpu1 PC
    ${address}=               Convert To Integer  ${pc.strip()}  16
    Should Be True            ${address} < 0x8000

*** Test Cases ***
Should Boot Zephyr
    [Documentation]           Clocks, resets, UART and the coprocessor instruction every core runs at start-up.
    Run Zephyr Sample         hello_world
    Wait For Line On Uart     Booting Zephyr OS
    Wait For Line On Uart     Hello World! rpi_pico2
    # Zephyr runs on core 0 only and leaves core 1 where the ROM parked it.
    Core 1 Should Wait In The Boot ROM

Should Schedule Threads On Time
    [Documentation]           Two threads take turns; SysTick has to run at the rate of clk_sys for the pace to be right.
    Run Zephyr Sample         synchronization
    Wait For Line On Uart     thread_a: Hello World from cpu 0
    ${first}=                 Wait For Line On Uart  thread_b: Hello World from cpu 0
    Wait For Line On Uart     thread_a: Hello World from cpu 0
    ${second}=                Wait For Line On Uart  thread_b: Hello World from cpu 0
    # Each thread busy-waits 100 ms and sleeps 500 ms before handing over.
    ${period}=                Evaluate  ${second.timestamp} - ${first.timestamp}
    Should Be True            1100 <= ${period} <= 1300

Should Blink The LED
    [Documentation]           GPIO goes through the GPIO coprocessor, the SIO and IO_BANK0 to the pin.
    Run Zephyr Sample         blinky
    Create LED Tester         ${LED}  defaultTimeout=2
    Wait For Line On Uart     LED state: ON
    Assert LED State          true
    Wait For Line On Uart     LED state: OFF
    Assert LED State          false
    Wait For Line On Uart     LED state: ON
    Assert LED State          true

Should Run The Shell
    [Documentation]           UART receive interrupts, and a prompt that needs input to come back.
    Run Zephyr Sample         shell_module
    Wait For Prompt On Uart   ${PROMPT}
    Write Line To Uart        kernel version
    Wait For Line On Uart     Zephyr version
    Wait For Prompt On Uart   ${PROMPT}
    Write Line To Uart        device list
    Wait For Line On Uart     uart@40070000 (READY)

Should Fire Timer Alarms
    [Documentation]           The counter driver arms TIMER0 alarms, which need the tick generator to count.
    Run Zephyr Sample         alarm
    Wait For Line On Uart     Set alarm in 2 sec
    Wait For Line On Uart     !!! Alarm !!!
    Wait For Line On Uart     Now: 2
    Wait For Line On Uart     Set alarm in 4 sec
    Wait For Line On Uart     !!! Alarm !!!
    Wait For Line On Uart     Now: 6

Should Reset On Watchdog Timeout
    [Documentation]           The watchdog counts ticks too, and its reset takes the whole chip down.
    Run Zephyr Sample         watchdog
    Wait For Line On Uart     Watchdog sample application
    Wait For Line On Uart     Waiting for reset...
    # The same banner again means the machine went through a reset.
    Wait For Line On Uart     Booting Zephyr OS
    Wait For Line On Uart     Watchdog sample application

Should Put Core 1 To Work From Zephyr
    [Documentation]           Zephyr launches a worker on core 1 and talks to it through its mailbox driver.
    Run Zephyr Sample         zephyr_core1
    Wait For Line On Uart     Zephyr on core 0
    Wait For Line On Uart     worker on core 1
    Wait For Line On Uart     5 squared is 25
    Wait For Line On Uart     core 1 answered 5 of 5 requests
