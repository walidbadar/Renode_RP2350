..
   Copyright (c) 2026 Muhammad Waleed Badar
   SPDX-License-Identifier: Apache-2.0

Renode_RP2350
#############

.. image:: images/renode-rp2350-banner.svg
   :alt: Renode_RP2350: both cores of the RP2350, in Renode
   :width: 720
   :align: center

A model of the Raspberry Pi RP2350 for `Renode <https://renode.io>`_ in
which both Arm Cortex-M33 cores run, with everything they need to work
together: the boot ROM's launch of core 1, the mailbox, doorbells and
spinlocks of the SIO, an interrupt controller for each core, and the reset
of core 1 through the PSM.

.. code-block:: none

   RP2350 dual-core sample
   core 0: running, CPUID 0
   core 1: running, CPUID 1
   launch core 1 ............ ok
   mailbox ping-pong ........ ok
   mailbox flags ............ ok
   spinlock ................. ok
   doorbell ................. ok
   per-core SysTick ......... ok
   timer alarm on core 1 .... ok
   core 1: running, CPUID 1
   reset and relaunch ....... ok
   all 8 checks passed

Features
********

- Two Cortex-M33 cores with TrustZone, each with its own NVIC and SysTick,
  starting from a boot ROM stand-in the way the chip does.
- Core 1 is launched with the mailbox sequence that the boot ROM defines, so
  ``multicore_launch_core1()`` of the Pico SDK works unchanged.
- SIO with per-core CPUID, both mailboxes, doorbells, spinlocks and GPIO;
  the GPIO coprocessor of each core.
- Clocks that follow the PLLs and dividers, so SysTick and the timers run
  at the rate the firmware programmed.
- Stock Zephyr boots on it: console, shell, GPIO, timers, watchdog.
- A Zephyr board for the model, and tests that Zephyr's twister runs on
  it: the dual-core checks on bare metal and from Zephyr, and the models
  one register at a time.

.. toctree::
   :maxdepth: 2
   :caption: Contents

   getting_started
   dual_core
   model
   samples
   testing
   debugging
   limitations
   licence
