..
   Copyright (c) 2026 Muhammad Waleed Badar
   SPDX-License-Identifier: Apache-2.0

Testing
#######

The tests are run by twister, Zephyr's test runner, on the ``cortex_m33``
board. From the top of the :ref:`workspace <workspace>`:

.. code-block:: sh

   west twister -p cortex_m33/rp2350a/m33_0 -T Renode_RP2350

Twister builds each test, starts it in Renode and puts the logs and the
reports in ``twister-out``.

.. list-table::
   :header-rows: 1
   :widths: 28 72

   * - Test
     - What it runs
   * - ``tests/multicore``
     - The bare-metal sample, with the cores in parallel and taking turns;
       both cores parked in the ROM when there is no image; resets from
       the Monitor, from software, and with core 1 held
   * - ``tests/peripherals``
     - The models one register at a time, from the Monitor, with no image
       loaded: aliases, resets, the clock tree, timers and alarms, the
       SIO, pins and pin interrupts, BOOTRAM, the PSM and the watchdog
   * - ``samples/zephyr_core1``
     - Zephyr on core 0 with a worker on core 1, judged by its console
       output

The first two are Robot Framework files, which twister hands to
``renode-test``, the test runner that comes with Renode. ``renode-test``
has to be on the ``PATH``; it is next to the ``renode`` executable in a
portable install, and the Python packages it needs are listed in
``tests/requirements.txt`` there. Each of the two also builds a Zephyr
image, because twister wants one, but the Robot files do not run it: they
use the boot ROM stand-in, and the bare-metal sample, that are built next
to it.

Stock Zephyr samples and tests run on the board the same way, with their
own pass criteria, on either variant. The SMP tests of the kernel are the
ones that show both cores at work under one scheduler:

.. code-block:: sh

   west twister -p cortex_m33/rp2350a/m33_0 \
       -T zephyr/samples/hello_world -T zephyr/samples/synchronization
   west twister -p cortex_m33/rp2350a/m33/smp \
       -T zephyr/tests/kernel/multiprocessing

To pass options to ``renode-test``, such as ``-n`` to repeat every test,
start it by hand on a build of the test:

.. code-block:: sh

   west build -b cortex_m33/rp2350a/m33_0 Renode_RP2350/tests/multicore
   renode-test --variable ELF:@$PWD/build/zephyr/zephyr.elf -n 5 \
       Renode_RP2350/tests/multicore/multicore.robot

Continuous integration
**********************

``.github/workflows/test.yml`` sets up the workspace and the Zephyr SDK,
installs Renode 1.16.0 and runs twister on both variants of the board: a
few stock Zephyr samples on each, the tests of this repository on the one
without SMP, and the SMP tests of the Zephyr kernel on the ``smp`` variant.
