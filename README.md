# Onyx-Random-Ish

A mod for **Kitten Space Agency (KSA)** that adds **pentaborane rocket fuel** and new engines to burn it.

Pentaborane (B5H9) was a real, high-energy rocket fuel investigated in the 1950s. It also happens to be
extremely toxic, and this mod takes that seriously: kittens who stand too close to a firing pentaborane
engine will not survive.

## What it adds

- **Pentaborane**, a new storable liquid fuel, with thermal properties based on published data.
- **Pentaborane + oxygen**, a new propellant mixture. Its chamber temperature, gamma and molar-mass
  tables were calculated with NASA's CEA equilibrium code across the same mixture-ratio and pressure
  grid the stock reactions use. Stoichiometric O/F is about 3.04, and the default mixture ratio is 2.0.
- **New parts**
  - **ORI 1m**, a 1 m tank
  - **ORI Engine 1A**, an engine
  - **ORI Engine 1**, an integrated tank and engine
- **Green exhaust plume**, a custom exhaust template (`EngineORI`). Boron burns green.
- **Toxic exhaust** (needs StarMap, see below): EVA kittens near a firing pentaborane engine build up
  exposure and are killed in action if they stay too long.

## How the toxicity works

- Only **EVA kittens** are affected. Kittens seated inside any crewed part are never touched, so capsules
  from other mods are safe by default.
- Exposure is measured from each **firing nozzle** on any vehicle burning pentaborane.
- While a kitten is within the danger radius, exposure builds up. Away from the zone, it falls off.
- When exposure reaches the limit, the kitten is killed using the game's own crew-kill path: the usual
  "killed in action" alert shows, the kitten is removed, and the roster marks them as lost.

The defaults are a **50 m** radius, **4 seconds** of exposure to kill, and exposure recovering at
**0.5 s per second** outside the zone. They are constants at the top of `ORI Code/ORIMod.cs`, and
setting `DryRun = true` makes the mod only log `WOULD KILL` instead of removing anyone.

## Requirements

- Kitten Space Agency
- **StarMap** (the code mod loader), needed for the toxicity part. The fuel, parts and plume are plain
  data mods and work without it, but kittens will not be harmed.

## Installation

1. Download this repository (Code, then Download ZIP) and unzip it.
2. **Rename the folder to `OnyxRandomIsh`.** The mod is identified by its folder name, and the zip
   unpacks as `Onyx-Random-Ish-main`, which will not work.
3. Move it to your KSA mods folder:

   ```
   Documents\My Games\Kitten Space Agency\mods\OnyxRandomIsh
   ```

4. Make sure the folder contains `mod.toml`, the `.xml` files, `OnyxRandomIsh.dll`,
   `OnyxRandomIsh.deps.json`, and the `Meshes` and `Textures` folders.
5. Enable the mod the same way as your other mods, and start the game through StarMap.

## Building from source

The code lives in the `ORI Code` folder.

1. Install the **.NET 10 SDK**.
2. Open `ORI Code/PentaboraneHazard.csproj` and change the `KsaDir` line to the folder that holds your
   `KSA.dll`, ending in a backslash:

   ```xml
   <KsaDir>C:\Path\To\Kitten Space Agency\</KsaDir>
   ```

3. In the `ORI Code` folder, run:

   ```
   dotnet build -c Release
   ```

4. Copy `OnyxRandomIsh.dll` and `OnyxRandomIsh.deps.json` from `ORI Code/bin/Release/net10.0/` into the
   mod folder (with the game closed).

The mod writes a log to `OnyxRandomIsh.log` in your Documents folder, which is useful for checking that the
toxicity code is running.

## Known limitations

- Ruptured tanks, venting and leaks are not modelled yet. The game has no leak system, so for now only
  running engines are dangerous.
- The danger zone is a plain sphere around each nozzle rather than following the exhaust direction.
- The combustion tables are ideal-equilibrium chamber values, like the stock ones. The game format has no
  place for condensed boron oxide, so real-world losses from it are not modelled.

## License

[MIT](LICENSE)
