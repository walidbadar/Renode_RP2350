# Copyright (c) 2026 Muhammad Waleed Badar
# SPDX-License-Identifier: GPL-3.0-only
#
# The dual-core checks written against the Raspberry Pi Pico SDK, built by
# "make pico-sdk". Everything that touches the hardware in this program is
# the SDK's own code, which makes it a second opinion on the model.
# The tests are skipped while the program has not been built.

*** Settings ***
Library                       OperatingSystem

*** Variables ***
${SCRIPT}                     ${CURDIR}/../scripts/rpi_pico2.resc
${ELF}                        ${CURDIR}/../build/pico_sdk/sdk_multicore.elf
${UART}                       sysbus.uart0

*** Keywords ***
Run SDK Sample
    ${built}=                 Run Keyword And Return Status  File Should Exist  ${ELF}
    Skip If                   not ${built}  The Pico SDK sample is not built; run "make pico-sdk"

    Execute Command           $elf=@${ELF}
    Execute Script            ${SCRIPT}
    Create Terminal Tester    ${UART}
    Start Emulation

Wait For All Checks
    Wait For Line On Uart     RP2350 dual-core sample, Pico SDK
    Wait For Line On Uart     core 0: running, core number 0
    Wait For Line On Uart     core 1: running, core number 1
    Wait For Line On Uart     launch core 1 ............ ok
    Wait For Line On Uart     mailbox ping-pong ........ ok
    Wait For Line On Uart     mutex .................... ok
    Wait For Line On Uart     doorbell ................. ok
    Wait For Line On Uart     sleep on both cores ...... ok
    Wait For Line On Uart     lockout .................. ok
    # Core 1 announces itself a second time after being reset.
    Wait For Line On Uart     core 1: running, core number 1
    Wait For Line On Uart     reset and relaunch ....... ok
    Wait For Line On Uart     all 7 checks passed

*** Test Cases ***
Should Pass All SDK Checks
    [Documentation]           The SDK's start-up, multicore_launch_core1(), mailbox, mutex, doorbell, sleep, lockout and multicore_reset_core1().
    Run SDK Sample
    Wait For All Checks

Should Pass All SDK Checks With Serial Execution
    [Documentation]           The same with the cores taking turns, which makes a run repeatable.
    Execute Command           emulation SetGlobalSerialExecution true
    Run SDK Sample
    Wait For All Checks

Should Pass All SDK Checks Without The WFE Stand-In
    [Documentation]           With WFE as a no-op the SDK needs no event the model cannot give it.
    ${built}=                 Run Keyword And Return Status  File Should Exist  ${ELF}
    Skip If                   not ${built}  The Pico SDK sample is not built; run "make pico-sdk"

    Execute Command           $elf=@${ELF}
    Execute Script            ${SCRIPT}
    Execute Command           cpu0 WfeAndSevAsNop true
    Execute Command           cpu1 WfeAndSevAsNop true
    Create Terminal Tester    ${UART}
    Start Emulation
    Wait For All Checks
