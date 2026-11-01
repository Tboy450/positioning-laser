# Positioning laser switch

Double-click **Positioning Laser.exe**. It starts disconnected and OFF. The single
button turns positioning pulses on and off; closing the window also requests OFF.
COM4 at 115200 is released when OFF, allowing the engraving sender to use it.
Escape, homing, recentering and print-resume commands are never sent.

## Fixed output

Only **1% commanded output (S10)** is available. There is no slider or automatic
increase if it is too dim. The app requires `$30=1000`, `$31=0`, `$32=1`, Idle and
zero reported feed/output. It writes M5 S0, checks the programmed zero, reads the
settings, resets the spindle override to 100%, then programs S10 while M5 is active
and reads it back **before every ON burst**. An unexpected/missing value blocks ON.
No settings are changed in EEPROM. Settings responses are not written to disk.

Pulses are 250 ms, each sent together with its own dwell and M5 S0 OFF command.
There is a brief dark interval for checking the controller between pulses. OFF
does not renew a pulse; the normal wait is the remaining fraction of a second
plus communication time. The switch automatically turns off after 60 seconds.
It preserves position/offsets and restores the earlier G0/G1 and feed, with S0.
Only one instance can run. A busy or held machine is refused without changing it.

This is the **engraving diode at low commanded power**, not a separate pointer.
It may be invisible at S10; a visible beam and a non-marking result are not
guaranteed. Low power still needs the normal laser eye protection and supervision.

## USB / fault behavior

Any controller fault or reset stops renewal. The app tries M5 S0 and verifies both
the modal state and the status report. If that fails, it attempts a soft reset and
shows **OFF NOT CONFIRMED**. Switch off the machine if that message appears. An
emergency reset can lose position; the app does not reconnect, re-declare position,
unlock or restart automatically. Opening a serial port can also reset some boards.

Queued OFF commands reduce dependence on a live app. They are **not a hardware
watchdog**: a partial USB delivery or firmware/hardware fault cannot be ruled out.
Controller readback confirms commands/state, not measured optical power. Don't
control the machine from Wi-Fi or another sender while using this switch.

## Build and verification

`build.ps1` builds a Windows .NET Framework application with the existing Windows
C# compiler; no downloads or Python packages are needed. It also runs simulated
protocol tests. `Positioning Laser.exe --preview <absolute-output.png>` renders
the OFF interface without opening any serial port. Test binaries do not use COM4.

Protocol references: [Grbl laser mode](https://github.com/gnea/grbl/blob/master/doc/markdown/laser_mode.md),
[Grbl commands / overrides](https://github.com/gnea/grbl/blob/master/doc/markdown/commands.md).
