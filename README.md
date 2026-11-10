# rp2350_renode

<p align="center">
  <img src="doc/images/rp2350-renode-banner.svg" alt="rp2350_renode: both cores of the RP2350, in Renode" width="720">
</p>

A model of the Raspberry Pi RP2350 for [Renode](https://renode.io) in which
both Arm Cortex-M33 cores run, with everything they need to work together:
the boot ROM's launch of core 1, the mailbox, doorbells and spinlocks of the
SIO, an interrupt controller for each core, and the reset of core 1 through
the PSM.

```
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
```

- Two Cortex-M33 cores with TrustZone, each with its own NVIC and SysTick,
  starting from a boot ROM stand-in the way the chip does.
- Core 1 is launched with the mailbox sequence that the boot ROM defines, so
  `multicore_launch_core1()` of the Pico SDK works unchanged.
- SIO with per-core CPUID, both mailboxes, doorbells, spinlocks and GPIO;
  the GPIO coprocessor of each core.
- Clocks that follow the PLLs and dividers, so SysTick and the timers run
  at the rate the firmware programmed.
- Stock Zephyr boots on it: console, shell, GPIO, timers, watchdog.
- A Zephyr board for the model, and tests that Zephyr's twister runs on
  it: the dual-core checks on bare metal and from Zephyr, and the models
  one register at a time.

## Quick start

Renode 1.16.0, `west` and the Zephyr SDK are needed. The repository is the
manifest of a west workspace, like the Zephyr example application:

```sh
west init -m https://github.com/walidbadar/rp2350_renode --mr main my-workspace
cd my-workspace
west update
```

Build a Zephyr application for the `cortex_m33` board and run it in Renode:

```sh
west build -b cortex_m33/rp2350a/m33_0 rp2350_renode/samples/zephyr_core1 -t run
```

Run the tests, with `renode-test` on the `PATH`:

```sh
west twister -p cortex_m33/rp2350a/m33_0 -T rp2350_renode
```

The full documentation is at <https://walidbadar.github.io/rp2350_renode/>.

- [Getting started](https://walidbadar.github.io/rp2350_renode/getting_started.html): requirements,
  building, [running another image](https://walidbadar.github.io/rp2350_renode/getting_started.html#running-another-image)
  and the repository layout.
- [The two cores](https://walidbadar.github.io/rp2350_renode/dual_core.html): reset, the
  [boot ROM stand-in](https://walidbadar.github.io/rp2350_renode/dual_core.html#the-boot-rom-stand-in) and the launch
  of core 1, the SIO, interrupts, coprocessors and `WFE`.
- [What is modelled](https://walidbadar.github.io/rp2350_renode/model.html), block by block, and what is not.
- [Samples](https://walidbadar.github.io/rp2350_renode/samples.html): bare metal and Zephyr.
- [Testing](https://walidbadar.github.io/rp2350_renode/testing.html),
  [Debugging](https://walidbadar.github.io/rp2350_renode/debugging.html) and
  [Limitations and troubleshooting](https://walidbadar.github.io/rp2350_renode/limitations.html).

The same pages are in [`doc/`](doc/).

## Licence

Apache-2.0. The boot ROM stand-in is not the Raspberry Pi boot ROM and shares
no code with it. See the [licence page](https://walidbadar.github.io/rp2350_renode/licence.html) for
what comes from elsewhere and for trademarks.
