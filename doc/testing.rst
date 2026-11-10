..
   Copyright (c) 2026 Muhammad Waleed Badar
   SPDX-License-Identifier: GPL-3.0-only

Testing
#######

The tests are Robot Framework files for ``renode-test``, the test runner
that comes with Renode.

.. code-block:: sh

   make test

builds the boot ROM and the bare-metal sample, creates a Python virtual
environment in ``.venv`` with the packages ``renode-test`` needs, and runs
every file in ``tests/``. Logs and the HTML report go to ``build/tests``.

.. list-table::
   :header-rows: 1
   :widths: 28 72

   * - File
     - What it runs
   * - ``multicore.robot``
     - The bare-metal sample, with the cores in parallel and taking turns;
       both cores parked in the ROM when there is no image; resets from
       the Monitor, from software, and with core 1 held
   * - ``peripherals.robot``
     - The models one register at a time, from the Monitor, with no image
       loaded: aliases, resets, the clock tree, timers and alarms, the
       SIO, pins and pin interrupts, BOOTRAM, the PSM and the watchdog
   * - ``pico_sdk.robot``
     - The Pico SDK sample
   * - ``zephyr.robot``
     - The Zephyr samples, stock and ``zephyr_core1``

The tests in the last two files are skipped until their images have been
built:

.. code-block:: sh

   make zephyr ZEPHYR_BASE=~/zephyrproject/zephyr
   make pico-sdk
   make test

To run some of the files, or to pass options to ``renode-test``, such as
``-n`` to repeat every test:

.. code-block:: sh

   make test TESTS=tests/multicore.robot
   make test TEST_ARGS="-n 5"

``renode-test`` is next to the ``renode`` executable in a portable install;
the Makefile finds it there.

Continuous integration
**********************

``.github/workflows/test.yml`` builds the boot ROM and the samples that
need no Zephyr workspace, and runs the tests on Renode 1.16.0. The Zephyr
tests are skipped there.
