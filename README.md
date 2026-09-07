# Much Bigger Backpack

## Description
Buy a bigger backpack at Pierre's for only 50,000g! This backpack has a configurable number of slots.

## Config
*Note: run Stardew Valley once with this mod enabled to generate the `config.json` file.*

* `BackpackSlots`: How many slots the premium backpack has. Must be a multiple of 12 from 48 to 72. Default = 48.
* `BackpackCost`: How much you have to pay to buy this backpack in the shop. Default = 50000.

## Console Commands
* `player_setbackpacksize slots`: Changes the size of your inventory. Valid values for slots are 12, 24, 36, or any multiple of 12 from 48 to 72. Items in the removed slots will be spilled onto the ground.

## Dependencies
This mod requires the following mods to be installed:

* [SMAPI](https://www.nexusmods.com/stardewvalley/mods/2400)
* [StardewHack](https://www.nexusmods.com/stardewvalley/mods/3213)

## Known bugs
This fork is based on the original Bigger Backpack mod.

* This mod does not support android / mobile.

## Uninstalling
To remove the mod, first run `player_setbackpacksize 36` in the SMAPI console and safe your game. 

## Changes
#### 6.0:
* Compatibility with upcoming SMAPI 4.0.
* Added setting `BackpackCost` to modify the cost of the bigger backpack.
* Added setting `BackpackSlots` to configure the premium backpack size.

#### 4.0:
* Add 64-bit support.

#### 3.0:
* Allow content patcher mods, such as [Garden Variety UI](https://www.nexusmods.com/stardewvalley/mods/3879) to override the custom assets used in this mod.
