# Renode_RP2350

<p align="center">
  <img src="doc/images/renode-rp2350-banner.svg" alt="Renode_RP2350: both cores of the RP2350, in Renode" width="720">
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
- Zephyr on both cores at once (`CONFIG_SMP`): one kernel schedules threads
  on either core, with core 1 started through the boot ROM, the doorbells
  as inter-processor interrupts and TIMER1 as a system timer both cores
  share.
- A Zephyr board for the model, and tests that Zephyr's twister runs on
  it: the dual-core checks on bare metal and from Zephyr, and the models
  one register at a time.

## Quick start

Renode 1.16.0, `west` and the Zephyr SDK are needed. The repository is the
manifest of a west workspace, like the Zephyr example application:

```sh
west init -m https://github.com/walidbadar/Renode_RP2350 --mr main my-workspace
cd my-workspace
west update
```

### Linking to an existing workspace

To use a Zephyr workspace you already have instead, link the repository
into it and make it the workspace's manifest:

```sh
cd ~/zephyrproject
ln -s /path/to/Renode_RP2350 Renode_RP2350
west config manifest.path Renode_RP2350
west update zephyr
```

`west update zephyr` checks out the Zephyr commit that `west.yml` pins, so
commit or stash any work in `zephyr/` first. Run `west update` without a
name instead if `cmsis_6` or `hal_rpi_pico` is missing or at another
revision. While the link is the manifest, only Zephyr and those two modules
are part of the workspace; to go back:

```sh
west config manifest.path zephyr
west update
```

Zephyr has no SMP for Cortex-M yet.
[zephyr#124714](https://github.com/zephyrproject-rtos/zephyr/pull/124714),
a draft pull request, adds it. `west.yml` pins Zephyr to
[a branch](https://github.com/walidbadar/zephyr/tree/rp2350_smp) that has the
commits of the pull request for the architecture code on a newer Zephyr,
and this repository adds what they need from the RP2350.

Three board targets run on the model. They follow Zephyr's naming for the
RP2350A: `m33_0` is Zephyr on core 0, and `m33` is the chip with a
Cortex-M33 in both sockets, which the `smp` variant uses.

| Target | Board | Zephyr runs on |
| --- | --- | --- |
| `cortex_m33/rp2350a/m33_0` | `cortex_m33`, the board for the model | Core 0; core 1 waits in the boot ROM until an application launches it |
| `cortex_m33/rp2350a/m33/smp` | `cortex_m33`, the board for the model | Both cores, one kernel |
| `rpi_pico2/rp2350a/m33/smp` | `rpi_pico2`, Zephyr's Raspberry Pi Pico 2 | Both cores, one kernel |

Build an application and run it in Renode, from the top of the workspace:

```sh
west build -b cortex_m33/rp2350a/m33_0 zephyr/samples/hello_world -t run
west build -p -b cortex_m33/rp2350a/m33/smp zephyr/samples/philosophers -t run
```

With `CONFIG_SCHED_CPU_MASK`, the synchronization sample pins one of its
two threads to each core, which shows both cores at work:

```sh
west build -p -b rpi_pico2/rp2350a/m33/smp zephyr/samples/synchronization -t run -- -DCONFIG_SCHED_CPU_MASK=y
```

```
*** Booting Zephyr OS build c3cd7868d47c ***
thread_a: Hello World from cpu 0 on rpi_pico2!
thread_b: Hello World from cpu 1 on rpi_pico2!
thread_a: Hello World from cpu 0 on rpi_pico2!
thread_b: Hello World from cpu 1 on rpi_pico2!
```

Run the tests, with `renode-test` on the `PATH`:

```sh
west twister -p cortex_m33/rp2350a/m33_0 -T Renode_RP2350
west twister -p cortex_m33/rp2350a/m33/smp -T zephyr/tests/kernel/multiprocessing
```

## What is in the repository

| Path | Contents |
| --- | --- |
| `west.yml` | The manifest of the workspace |
| `zephyr/` | What makes the repository a Zephyr module |
| `boards/renode/cortex_m33/` | The Zephyr board for the model, with its `smp` variant |
| `boards/raspberrypi/rpi_pico2/` | The `smp` variant of Zephyr's Pico 2 board |
| `soc/rp2350/` | What Zephyr needs from the RP2350 to run on both cores |
| `platforms/`, `peripherals/`, `scripts/` | The Renode model: platform descriptions, C# models and scripts |
| `bootrom/` | The boot ROM stand-in |
| `samples/`, `tests/` | Samples, and the tests twister runs |
| `doc/` | The documentation |

The full documentation is at <https://walidbadar.github.io/Renode_RP2350/>.

- [Getting started](https://walidbadar.github.io/Renode_RP2350/getting_started.html): requirements,
  building, [running another image](https://walidbadar.github.io/Renode_RP2350/getting_started.html#running-another-image)
  and the repository layout.
- [The two cores](https://walidbadar.github.io/Renode_RP2350/dual_core.html): reset, the
  [boot ROM stand-in](https://walidbadar.github.io/Renode_RP2350/dual_core.html#the-boot-rom-stand-in) and the launch
  of core 1, the SIO, interrupts, coprocessors and `WFE`.
- [What is modelled](https://walidbadar.github.io/Renode_RP2350/model.html), block by block, and what is not.
- [Samples](https://walidbadar.github.io/Renode_RP2350/samples.html): bare metal and Zephyr.
- [Testing](https://walidbadar.github.io/Renode_RP2350/testing.html),
  [Debugging](https://walidbadar.github.io/Renode_RP2350/debugging.html) and
  [Limitations and troubleshooting](https://walidbadar.github.io/Renode_RP2350/limitations.html).

The same pages are in [`doc/`](doc/).

## Licence

Apache-2.0. The boot ROM stand-in is not the Raspberry Pi boot ROM and shares
no code with it. See the [licence page](https://walidbadar.github.io/Renode_RP2350/licence.html) for
what comes from elsewhere and for trademarks.
