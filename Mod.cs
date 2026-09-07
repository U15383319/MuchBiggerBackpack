using System.Collections.Generic;
using System.Reflection.Emit;
using System;
using System.Reflection;
using HarmonyLib;
using StardewModdingAPI;
using StardewValley.Menus;
using StardewValley;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley.Objects;
using StardewHack;
using StardewValley.Locations;
using StardewModdingAPI.Events;
using GenericModConfigMenu;

namespace MuchBiggerBackpack
{
    public class ModConfig {
        /** How much you have to pay to buy this backpack in the shop. (default = 50000).*/
        public int BackpackCost = 50000;

        /** Target backpack size. Must be a multiple of 12 between 48 and 72. */
        public int BackpackSlots = 48;
    }
    
    public class Mod : HackWithConfig<Mod,ModConfig>
    {
        public static Mod instance;

        private const int VanillaBackpackSlots = 36;
        private const int BackpackSlotsPerRow = 12;
        private const int MinimumBackpackSlots = 48;
        private const int MaximumBackpackSlots = 72;
        private const int BaseMenuHeight = 600;

        private static Texture2D bigBackpack;
        private static bool loggedInventoryPageJunimoNotePosition;
        private Harmony compatibilityHarmony;
        private bool patchedUiInfoSuiteBillboard;

        /// <summary>The mod entry point, called after the mod is first loaded.</summary>
        /// <param name="helper">Provides simplified APIs for writing mods.</param>
        public override void HackEntry(IModHelper helper)
        {
            sanitizeConfig();

            bigBackpack = Helper.ModContent.Load<Texture2D>("backpack.png");
            helper.Events.Content.AssetRequested += this.OnAssetRequested;
            helper.Events.Display.RenderingActiveMenu += this.OnRenderingActiveMenu;
            helper.Events.GameLoop.GameLaunched += this.OnGameLaunched;

            Helper.ConsoleCommands.Add("player_setbackpacksize", $"Set the size of the player's backpack. This must be 12, 24, 36, or a multiple of 12 up to {MaximumBackpackSlots}.", command);
            
            Patch((SeedShop s)=>s.draw(null), SeedShop_draw);
            Patch((SpecialItem si)=>si.getTemporarySpriteForHoldingUp(new Vector2()), SpecialItem_getTemporarySpriteForHoldingUp);
            Patch((GameLocation gl)=>gl.performAction(new string[0], null, new xTile.Dimensions.Location()), GameLocation_performAction);
            Patch((GameLocation gl)=>gl.answerDialogueAction("", null), GameLocation_answerDialogueAction);
            Patch(()=>new InventoryPage(0,0,0,0), InventoryPage_ctor);
            Patch((InventoryPage ip)=>ip.draw(null), InventoryPage_draw);
            Patch(()=>new CraftingPage(0,0,0,0,false,false,null), CraftingPage_ctor);
            Patch(typeof(ShopMenu), "Initialize", ShopMenu_Initialize);
            Patch((ShopMenu m)=>m.draw(null), ShopMenu_draw);
            Patch((ShopMenu m)=>m.drawCurrency(null), ShopMenu_drawCurrency);
            Patch(()=>new MenuWithInventory(null,false,false,0,0,0,ItemExitBehavior.Drop,false), ShippingMenu_ctor);
            Patch((JunimoNoteMenu m)=>m.setUpMenu(0,null), JunimoNoteMenu_setUpMenu);
        }

        private void sanitizeConfig() {
            int normalizedSlots = normalizeBackpackSlots(config.BackpackSlots);
            if (config.BackpackSlots != normalizedSlots) {
                Monitor.Log($"BackpackSlots must be a multiple of {BackpackSlotsPerRow} between {MinimumBackpackSlots} and {MaximumBackpackSlots}. Using {normalizedSlots}.", LogLevel.Warn);
                config.BackpackSlots = normalizedSlots;
                Helper.WriteConfig(config);
            }
        }

        private static int normalizeBackpackSlots(int slots) {
            slots = (slots / BackpackSlotsPerRow) * BackpackSlotsPerRow;
            if (slots < MinimumBackpackSlots)
                return MinimumBackpackSlots;
            if (slots > MaximumBackpackSlots)
                return MaximumBackpackSlots;
            return slots;
        }

        private static bool isValidBackpackSize(int slots) {
            return slots == BackpackSlotsPerRow
                || slots == BackpackSlotsPerRow * 2
                || slots == VanillaBackpackSlots
                || (slots >= MinimumBackpackSlots && slots <= MaximumBackpackSlots && slots % BackpackSlotsPerRow == 0);
        }

        public static int getBackpackSlots() => normalizeBackpackSlots(getInstance().config.BackpackSlots);

        public static int getBackpackRows() => getBackpackSlots() / BackpackSlotsPerRow;

        public static int getInventorySlots() {
            int slots = getBackpackSlots();
            if (Game1.player != null && Game1.player.MaxItems > slots) {
                slots = normalizeBackpackSlots(Game1.player.MaxItems);
            }
            return slots;
        }

        public static int getInventoryRows() => getInventorySlots() / BackpackSlotsPerRow;

        public static int getBackpackExtraRows() => getInventoryRows() - VanillaBackpackSlots / BackpackSlotsPerRow;

        public static int getBackpackExtraHeight() => getBackpackExtraRows() * Game1.tileSize;

        public static int getMenuHeight() => BaseMenuHeight + getBackpackExtraHeight();

        public static int getCurrencyYOffset() => getBackpackExtraHeight() - 12;

        public static int getInventoryPagePortraitYOffset() => 256 + getBackpackExtraHeight();

        public static int getInventoryPageJunimoNoteYOffset() => 96 + getBackpackExtraHeight();

        public static int getUiInfoSuiteBillboardYOffset() {
            IClickableMenu menu = Game1.activeClickableMenu;

            if (menu is GameMenu gameMenu && gameMenu.GetCurrentPage() is InventoryPage inventoryPage && inventoryPage.inventory != null) {
                int menuBottom = menu.yPositionOnScreen + menu.height;
                int targetY = inventoryPage.inventory.yPositionOnScreen + inventoryPage.inventory.height + 12;
                int lowestSafeTop = menuBottom - 80;

                if (lowestSafeTop > menu.yPositionOnScreen)
                    targetY = Math.Min(targetY, lowestSafeTop);

                return Math.Max(0, menuBottom - targetY);
            }

            int extraRows = Math.Max(0, getBackpackExtraRows());
            return Math.Max(80, 300 - extraRows * 70);
        }

        public static int getJunimoNoteInventoryRows() {
            int rows = 1;
            int slots = getInventorySlots();
            while (rows * rows < slots)
                ++rows;
            return rows;
        }

        public static int getJunimoNoteInventoryCapacity() {
            int rows = getJunimoNoteInventoryRows();
            return rows * rows;
        }

        public static int getJunimoNoteInventoryGap() => getJunimoNoteInventoryRows() > 7 ? 0 : 4;

        public static int getJunimoNoteInventoryXOffset() => getJunimoNoteInventoryRows() > 7 ? 72 : 104;

        public static int getJunimoNoteInventoryYOffset() => getJunimoNoteInventoryRows() > 7 ? 96 : 120;

        private void command( string cmd, string[] args )
        {
            if (args.Length != 1)
            {
                Monitor.Log("Must have one command argument", LogLevel.Error);
                return;
            }

            // Parse the new size. Make sure it's a valid size.
            if (!int.TryParse(args[0], out int newMax) || !isValidBackpackSize(newMax)) {
                Monitor.Log($"The new size must be 12, 24, 36, or a multiple of 12 from {MinimumBackpackSlots} to {MaximumBackpackSlots}.", LogLevel.Error);
                return;
            }
            
            if (newMax < Game1.player.Items.Count)
            {
                // Shrink the inventory, spilling any items in the removed slots to the ground.
                for (int i = Game1.player.Items.Count - 1; i >= newMax; --i) {
                    if (Game1.player.Items[i] != null) {
                        // Spill the item.
                        Game1.createItemDebris(Game1.player.Items[i], Game1.player.getStandingPosition(), Game1.player.getDirection(), null, -1);
                    }
                    Game1.player.Items.RemoveAt(i);
                }
            }
            else
            {
                // Grow the inventory.
                for (int i = Game1.player.Items.Count; i < newMax; ++i) {
                    Game1.player.Items.Add(null);
                }
            }
            Game1.player.MaxItems = newMax;
        }

#region Draw Premium Backpack
        public static void drawBiggerBackpack(SpriteBatch b) {
            b.Draw(bigBackpack, Game1.GlobalToLocal(new Vector2 (456f, 1088f)), new Rectangle(0, 0, 12, 14), Color.White, 0.0f, Vector2.Zero, Game1.pixelZoom, SpriteEffects.None, (float)(19.25 * Game1.tileSize / 10000.0));
        }

        // Inject code for rendering the larger backpack in the shop.
        void SeedShop_draw() {
            var check = FindCode(
                Instructions.Call_get(typeof(Game1), nameof(Game1.player)),
                Instructions.Ldfld(typeof(Farmer), nameof(Farmer.maxItems)),
                OpCodes.Call,
                Instructions.Ldc_I4_S(36),
                OpCodes.Bge
            );
            
            var pos = check.Follow(4);
            
            // Do a sanity check
            if ((pos[-1].opcode == OpCodes.Ret || pos[-1].opcode == OpCodes.Br) &&
                  pos[0].opcode == OpCodes.Ldarg_1) {
                // Ok
            } else {
                throw new System.Exception("Jump does not go to expected location.");
            }
            
            // Inject check and call to drawBiggerBackpack.
            pos.Insert(0,
                // else if (maxItems < getBackpackSlots())
                Instructions.Call_get(typeof(Game1), nameof(Game1.player)),
                Instructions.Ldfld(typeof(Farmer), nameof(Farmer.maxItems)),
                check[2], // Nothing jumps here so reusing this should be OK.
                Instructions.Call(typeof(Mod), nameof(Mod.getBackpackSlots)),
                check[4], // We'll create a new jump in check later.
                // drawBiggerBackpack(b);
                Instructions.Ldarg_1(),
                Instructions.Call(typeof(Mod), nameof(Mod.drawBiggerBackpack), typeof(SpriteBatch))
                // }
            );
            
            // Create a new jump in check.
            check[4] = Instructions.Bge(AttachLabel(pos[0]));
        }
        
        static public TemporaryAnimatedSprite getBackpackSprite(Vector2 position) {
            return new TemporaryAnimatedSprite (null, new Rectangle(1, 0, 11, 13), position + new Vector2 (16f, 0f), false, 0f, Color.White) {
                scale = 4f,
                layerDepth = 1f,
                texture = bigBackpack
            };
        }
        
        // Inject code for rendering the larger backpack when picked up.
        void SpecialItem_getTemporarySpriteForHoldingUp() {
            var code = FindCode(
                Instructions.Ldstr("LooseSprites\\Cursors"),
                Instructions.Call_get(typeof(Game1), nameof(Game1.player)),
                Instructions.Ldfld(typeof(Farmer), nameof(Farmer.maxItems)),
                OpCodes.Call,
                Instructions.Ldc_I4_S(36)
                // Beq or Bne_un
            );
            
            code.Prepend(
                // if (maxItems==getBackpackSlots()) {
                Instructions.Call_get(typeof(Game1), nameof(Game1.player)),
                Instructions.Ldfld(typeof(Farmer), nameof(Farmer.maxItems)),
                code[3], // Nothing jumps here so reusing this should be OK.
                Instructions.Call(typeof(Mod), nameof(Mod.getBackpackSlots)),
                Instructions.Bne_Un(AttachLabel(code[0])),
                // return getBackpackSprite(position);
                Instructions.Ldarg_1(),
                Instructions.Call(typeof(Mod), nameof(Mod.getBackpackSprite), typeof(Vector2)),
                Instructions.Ret()
                // }
            );
        }
#endregion

#region Buy Backpack
        static public void clickBackpack()
        {
            Response yes = new Response("Purchase", string.Format("Purchase ({0:N0}g)", getBackpackCost()));
            Response no = new Response("Not", Game1.content.LoadString("Strings\\Locations:SeedShop_BuyBackpack_ResponseNo"));
            Response[] resps = new Response[] { yes, no };
            Game1.currentLocation.createQuestionDialogue($"Backpack Upgrade -- {getBackpackSlots()} slots", resps, "Backpack");
        }
        
        // Inject code to show the buying dialogue when the premium backpack  is clicked.
        void GameLocation_performAction() {
            var code = FindCode(
                Instructions.Call_get(typeof(Game1), nameof(Game1.player)),
                Instructions.Ldfld(typeof(Farmer), nameof(Farmer.maxItems)),
                OpCodes.Call,
                Instructions.Ldc_I4_S(36),
                OpCodes.Bge
            );
            code.Extend(
                Instructions.Ldstr("Backpack"),
                OpCodes.Call,
                OpCodes.Br
            );
            var len = code.length;
            code.Append(
                Instructions.Call_get(typeof(Game1), nameof(Game1.player)),
                Instructions.Ldfld(typeof(Farmer), nameof(Farmer.maxItems)),
                code[2],
                Instructions.Call(typeof(Mod), nameof(Mod.getBackpackSlots)),
                code[4],
                Instructions.Call(typeof(Mod), nameof(Mod.clickBackpack)),
                Instructions.Br((Label)code[len-1].operand)
            );
            code[4] = Instructions.Bge(AttachLabel(code[len]));
        }

        static void buyBackpack() {
            Game1.player.Money -= getBackpackCost();
            Game1.player.holdUpItemThenMessage((Item)new SpecialItem(99, "Premium Pack"), true);

            int slotsToAdd = getBackpackSlots() - Game1.player.MaxItems;
            while (slotsToAdd > 0) {
                Game1.player.increaseBackpackSize(BackpackSlotsPerRow);
                slotsToAdd -= BackpackSlotsPerRow;
            }
            // Game1.multiplayer.globalChatInfoMessage ("BackpackDeluxe", Game1.player.Name);
        }

        public static int getBackpackCost() => getInstance().config.BackpackCost;
        
        // Inject code for rendering the larger backpack when picked up.
        void GameLocation_answerDialogueAction() {
            var code = FindCode(
                // else if ((int)Game1.player.maxItems != 36) {
                Instructions.Call_get(typeof(Game1), nameof(Game1.player)),
                Instructions.Ldfld(typeof(Farmer), nameof(Farmer.maxItems)),
                OpCodes.Call,
                Instructions.Ldc_I4_S(36),
                OpCodes.Beq
            );
            var get_player = Instructions.Call_get(typeof(Game1), nameof(Game1.player));
            code.Replace(
                // else if ((int)Game1.player.maxItems < getBackpackSlots()
                Instructions.Call_get(typeof(Game1), nameof(Game1.player)),
                Instructions.Ldfld(typeof(Farmer), nameof(Farmer.maxItems)),
                code[2],
                Instructions.Call(typeof(Mod), nameof(Mod.getBackpackSlots)),
                Instructions.Bge(AttachLabel(get_player)),
                //   && Game1.player.Money >= Mod.getBackpackCost()) {
                Instructions.Call_get(typeof(Game1), nameof(Game1.player)),
                Instructions.Callvirt_get(typeof(Farmer), nameof(Farmer.Money)),
                Instructions.Call(typeof(Mod), nameof(Mod.getBackpackCost)),
                Instructions.Blt(AttachLabel(get_player)),
                //   buyBackpack();
                Instructions.Call(typeof(Mod), nameof(Mod.buyBackpack)),
                // }
                // else if ((int)Game1.player.maxItems != getBackpackSlots()) {
                get_player,
                Instructions.Ldfld(typeof(Farmer), nameof(Farmer.maxItems)),
                code[2],
                Instructions.Call(typeof(Mod), nameof(Mod.getBackpackSlots)),
                code[4]
            );
        }
        
#endregion

#region Compatibility
        private void OnGameLaunched(object sender, GameLaunchedEventArgs e) {
            patchUiInfoSuiteBillboard();
        }

        private void patchUiInfoSuiteBillboard() {
            if (patchedUiInfoSuiteBillboard)
                return;

            try {
                Type type = AccessTools.TypeByName("UIInfoSuite2.UIElements.ShowCalendarAndBillboardOnGameMenuButton");
                MethodInfo drawBillboard = type != null ? AccessTools.Method(type, "DrawBillboard") : null;

                if (drawBillboard == null) {
                    if (Helper.ModRegistry.IsLoaded("Annosz.UiInfoSuite2"))
                        Monitor.Log("UI Info Suite 2 is loaded, but its calendar/billboard draw method was not found.", LogLevel.Warn);

                    return;
                }

                compatibilityHarmony ??= new Harmony($"{ModManifest.UniqueID}.Compatibility");
                compatibilityHarmony.Patch(
                    original: drawBillboard,
                    transpiler: new HarmonyMethod(typeof(Mod), nameof(UiInfoSuiteDrawBillboard_Transpiler))
                );

                patchedUiInfoSuiteBillboard = true;
                Monitor.Log("Enabled UI Info Suite 2 calendar/billboard button position compatibility.", LogLevel.Info);
            } catch (Exception err) {
                Monitor.Log("Failed to patch UI Info Suite 2 calendar/billboard button position.", LogLevel.Warn);
                LogException(err, LogLevel.Warn);
            }
        }

        public static IEnumerable<CodeInstruction> UiInfoSuiteDrawBillboard_Transpiler(IEnumerable<CodeInstruction> instructions) {
            var code = new List<CodeInstruction>(instructions);

            for (int i = 0; i < code.Count; ++i) {
                if (code[i].opcode == OpCodes.Ldstr && code[i].operand is string modId && modId == "spacechase0.BiggerBackpack") {
                    int start = i - 3;
                    int removeCount = 9;

                    if (start < 0 || start + removeCount > code.Count)
                        break;

                    var replacement = new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Mod), nameof(getUiInfoSuiteBillboardYOffset)));
                    replacement.labels.AddRange(code[start].labels);
                    replacement.blocks.AddRange(code[start].blocks);
                    code[start].labels.Clear();
                    code[start].blocks.Clear();

                    code.RemoveRange(start, removeCount);
                    code.Insert(start, replacement);

                    return code;
                }
            }

            getInstance().Monitor.Log("Could not find UI Info Suite 2's Bigger Backpack offset branch; the calendar/billboard button may still overlap the expanded backpack.", LogLevel.Warn);
            return code;
        }
#endregion

#region Resize GUI 
        public static void shiftIconsDown(List<ClickableComponent> equipmentIcons){
            foreach (var icon in equipmentIcons) {
                icon.bounds.Y += getBackpackExtraHeight();
            }
        }

        public static void shiftInventoryPageComponents(InventoryPage page) {
            int offset = getBackpackExtraHeight();

            foreach (var icon in page.equipmentIcons) {
                icon.bounds.Y += offset;
            }

            if (page.organizeButton != null)
                page.organizeButton.bounds.Y += offset;

            if (page.trashCan != null)
                page.trashCan.bounds.Y += offset;
        }

        public static void positionInventoryPageJunimoNoteIcon(InventoryPage page) {
            if (page.junimoNoteIcon == null)
                return;

            int targetY = page.yPositionOnScreen + 96 + getBackpackExtraHeight();

            if (!loggedInventoryPageJunimoNotePosition && page.junimoNoteIcon.bounds.Y != targetY) {
                loggedInventoryPageJunimoNotePosition = true;
                getInstance().Monitor.Log($"Moving inventory page Junimo note icon from Y={page.junimoNoteIcon.bounds.Y} to Y={targetY}.", LogLevel.Info);
            }

            page.junimoNoteIcon.bounds.Y = targetY;
        }

        private void OnRenderingActiveMenu(object sender, RenderingActiveMenuEventArgs e) {
            if (Game1.activeClickableMenu is GameMenu gameMenu) {
                if (gameMenu.GetCurrentPage() is InventoryPage inventoryPage)
                    positionInventoryPageJunimoNoteIcon(inventoryPage);
            } else if (Game1.activeClickableMenu is InventoryPage inventoryPage) {
                positionInventoryPageJunimoNoteIcon(inventoryPage);
            }
        }

        void resize_inventory() {
            // Change inventory size from default (36) to the configured size.
            var inv = FindCode(
                OpCodes.Ldc_I4_M1,  // Size (-1 = default)
                OpCodes.Ldc_I4_3,   // Rows
                OpCodes.Ldc_I4_0,
                OpCodes.Ldc_I4_0,
                OpCodes.Ldc_I4_1
            );
            inv[0] = Instructions.Call(typeof(Mod), nameof(Mod.getInventorySlots));
            inv[1] = Instructions.Call(typeof(Mod), nameof(Mod.getInventoryRows));
        }
        
        void InventoryPage_ctor() {
            BeginCode().Prepend(
                // height += getBackpackExtraHeight();
                Instructions.Ldarg_S(4),
                Instructions.Call(typeof(Mod), nameof(Mod.getBackpackExtraHeight)),
                Instructions.Add(),
                Instructions.Starg_S(4)
            );
            
            resize_inventory();

            try {
                // Move the Community Center / powers board shortcut below the expanded inventory grid.
                FindCode(
                    Instructions.Call(typeof(InventoryPage), nameof(InventoryPage.ShouldShowJunimoNoteIcon)),
                    OpCodes.Brfalse,
                    OpCodes.Ldarg_0,
                    Instructions.Ldstr(""),
                    OpCodes.Ldarg_0,
                    Instructions.Ldfld(typeof(IClickableMenu), nameof(IClickableMenu.xPositionOnScreen)),
                    OpCodes.Ldarg_3,
                    OpCodes.Add,
                    OpCodes.Ldarg_0,
                    Instructions.Ldfld(typeof(IClickableMenu), nameof(IClickableMenu.yPositionOnScreen)),
                    Instructions.Ldc_I4_S(96),
                    OpCodes.Add
                )[10] = Instructions.Call(typeof(Mod), nameof(Mod.getInventoryPageJunimoNoteYOffset));
            } catch (System.Exception err) {
                Monitor.Log("Failed to fix inventory page Junimo note icon position.", LogLevel.Warn);
                LogException(err, LogLevel.Warn);
            }
            
            EndCode().Insert(-1,
                // Shift inventory-page side components down by the extra inventory height.
                Instructions.Ldarg_0(),
                Instructions.Call(typeof(Mod), nameof(Mod.shiftInventoryPageComponents), typeof(InventoryPage))
            );
            EndCode().ReplaceJump(-1, EndCode()[-3]);
            
            try {
                // Move portrait down with the expanded inventory.
                // This only affects where the tooltip shows up.
                FindCode(
                    OpCodes.Ldarg_0,
                    Instructions.Ldfld(typeof(IClickableMenu), nameof(IClickableMenu.yPositionOnScreen)),
                    Instructions.Ldsfld(typeof(IClickableMenu), nameof(IClickableMenu.borderWidth)),
                    OpCodes.Add,
                    Instructions.Ldsfld(typeof(IClickableMenu), nameof(IClickableMenu.spaceToClearTopBorder)),
                    OpCodes.Add,
                    Instructions.Ldc_I4(256),
                    OpCodes.Add,
                    Instructions.Ldc_I4_8(),
                    OpCodes.Sub,
                    Instructions.Ldc_I4_S(64),
                    OpCodes.Add
                )[6] = Instructions.Call(typeof(Mod), nameof(Mod.getInventoryPagePortraitYOffset));
            } catch (System.Exception err) {
                Monitor.Log("Failed to fix portrait tooltip position.", LogLevel.Warn);
                LogException(err, LogLevel.Warn);
            }
        }

        void InventoryPage_draw() {
            var code = BeginCode();
            
            // var yoffset = yPositionOnScreen + borderWidth + spaceToClearTopBorder + getBackpackExtraHeight()
            var yoffset = generator.DeclareLocal(typeof(int));
            code.Prepend(
                Instructions.Ldarg_0(),
                Instructions.Call(typeof(Mod), nameof(Mod.positionInventoryPageJunimoNoteIcon), typeof(InventoryPage)),
                Instructions.Ldarg_0(),
                Instructions.Ldfld(typeof(IClickableMenu), nameof(IClickableMenu.yPositionOnScreen)),
                Instructions.Ldsfld(typeof(IClickableMenu), nameof(IClickableMenu.borderWidth)),
                Instructions.Add(),
                Instructions.Ldsfld(typeof(IClickableMenu), nameof(IClickableMenu.spaceToClearTopBorder)),
                Instructions.Add(),
                Instructions.Call(typeof(Mod), nameof(Mod.getBackpackExtraHeight)),
                Instructions.Add(),
                Instructions.Stloc_S(yoffset)
            );
            
            // Replace all remaining `yPositionOnScreen + borderWidth + spaceToClearTopBorder` by `yoffset`.
            int yoffsetCounter = 0;
            try {
                while (true) {
                    code = code.FindNext(
                        OpCodes.Ldarg_0,
                        Instructions.Ldfld(typeof(IClickableMenu), nameof(IClickableMenu.yPositionOnScreen)),
                        Instructions.Ldsfld(typeof(IClickableMenu), nameof(IClickableMenu.borderWidth)),
                        OpCodes.Add,
                        Instructions.Ldsfld(typeof(IClickableMenu), nameof(IClickableMenu.spaceToClearTopBorder)),
                        OpCodes.Add
                    );
                    code.Replace(
                        Instructions.Ldloc_S(yoffset)
                    );
                    yoffsetCounter++;
                }
            } catch (InstructionNotFoundException) {
                if (yoffsetCounter == 0)
                    throw;

                if (yoffsetCounter != 9) {
                    Monitor.Log($"Replaced yoffset {yoffsetCounter} times instead of expected 9 times.", LogLevel.Warn);
                }
            }
        }

        void CraftingPage_ctor() {
            // Make the crafting page higher too, to accommodate the bigger inventory.
            BeginCode().Prepend(
                // height += getBackpackExtraHeight();
                Instructions.Ldarg_S(4),
                Instructions.Call(typeof(Mod), nameof(Mod.getBackpackExtraHeight)),
                Instructions.Add(),
                Instructions.Starg_S(4)
            );
            
            resize_inventory();
        }
        
        void ShopMenu_Initialize() {
            resize_inventory();

            // Fix the size of the shop buttons.
            // Replace `((height - 256) / 4)` with 106
            for (int i=0; i<2; i++) {
                var code = FindCode(
                    OpCodes.Ldarg_0,
                    Instructions.Ldfld(typeof(IClickableMenu), nameof(IClickableMenu.height)),
                    Instructions.Ldc_I4(256),
                    OpCodes.Sub,
                    Instructions.Ldc_I4_4(),
                    OpCodes.Div
                );
                code.Replace(
                    Instructions.Ldc_I4(106)
                );
            }
        }
        
        void ShopMenu_draw() {
            // Position the inventory background
            // Change `yPositionOnScreen + height - 256 + 40` to `yPositionOnScreen + 464`
            // Note: originally height = 680.
            FindCode(
                OpCodes.Ldarg_0,
                Instructions.Ldfld(typeof(IClickableMenu), nameof(IClickableMenu.yPositionOnScreen)),
                OpCodes.Ldarg_0,
                Instructions.Ldfld(typeof(IClickableMenu), nameof(IClickableMenu.height)),
                OpCodes.Add,
                Instructions.Ldc_I4(256),
                OpCodes.Sub,
                Instructions.Ldc_I4_S(40),
                OpCodes.Add
            ).SubRange(2,6).Replace(
                Instructions.Ldc_I4(464)
            );
            // Change `height - 448 + 20` to `inventory.height + 44`
            // Note: originally inventory.height = 3*64+16 = 208.
            FindCode(
                OpCodes.Ldarg_0,
                Instructions.Ldfld(typeof(IClickableMenu), nameof(IClickableMenu.height)),
                Instructions.Ldc_I4(448),
                OpCodes.Sub,
                Instructions.Ldc_I4_S(20),
                OpCodes.Add
            ).Replace(
                Instructions.Ldarg_0(),
                Instructions.Ldfld(typeof(ShopMenu), nameof(ShopMenu.inventory)),
                Instructions.Ldfld(typeof(IClickableMenu), nameof(IClickableMenu.height)),
                Instructions.Ldc_I4_S(44),
                Instructions.Add()
            );
            
            // Position the shop stock background
            // Change `height - 256 + 32 + 4` to 460.
            FindCode(
                OpCodes.Ldarg_0,
                Instructions.Ldfld(typeof(IClickableMenu), nameof(IClickableMenu.height)),
                Instructions.Ldc_I4(256),
                OpCodes.Sub,
                Instructions.Ldc_I4_S(32),
                OpCodes.Add,
                Instructions.Ldc_I4_4(),
                OpCodes.Add
            ).Replace(
                Instructions.Ldc_I4(460)
            );
        }

        void ShopMenu_drawCurrency() {
            FindCode(
                Instructions.Ldc_I4_S(12),
                OpCodes.Sub
            ).Replace(
                Instructions.Call(typeof(Mod), nameof(Mod.getCurrencyYOffset)),
                Instructions.Add()
            );
        }
        
        void ShippingMenu_ctor() {
            resize_inventory();
            
            var code = BeginCode();
            for (int i=0; i<2; i++) {
                code = code.FindNext(
                    Instructions.Ldc_I4(600),
                    Instructions.Ldsfld(typeof(IClickableMenu), nameof(IClickableMenu.borderWidth)),
                    Instructions.Ldc_I4_2(),
                    OpCodes.Mul,
                    OpCodes.Add
                );
                code[0] = Instructions.Call(typeof(Mod), nameof(Mod.getMenuHeight));
            }
        }
        
        void JunimoNoteMenu_setUpMenu() {
            // Move the grid a few pixels
            var code = FindCode(
                OpCodes.Ldarg_0,
                Instructions.Ldfld(typeof(IClickableMenu), nameof(IClickableMenu.xPositionOnScreen)),
                Instructions.Ldc_I4(128),
                OpCodes.Add,
                OpCodes.Ldarg_0,
                Instructions.Ldfld(typeof(IClickableMenu), nameof(IClickableMenu.yPositionOnScreen)),
                Instructions.Ldc_I4(140),
                OpCodes.Add
            );
            code[2] = Instructions.Call(typeof(Mod), nameof(Mod.getJunimoNoteInventoryXOffset));
            code[6] = Instructions.Call(typeof(Mod), nameof(Mod.getJunimoNoteInventoryYOffset));

            // Change inventory from a 6x6 grid to a grid large enough for the configured backpack.
            FindCode(
                Instructions.Ldc_I4_S(36),
                OpCodes.Ldc_I4_6,
                OpCodes.Ldc_I4_8,
                OpCodes.Ldc_I4_8,
                OpCodes.Ldc_I4_0
            ).Replace(
                Instructions.Call(typeof(Mod), nameof(Mod.getJunimoNoteInventoryCapacity)),
                Instructions.Call(typeof(Mod), nameof(Mod.getJunimoNoteInventoryRows)),
                Instructions.Call(typeof(Mod), nameof(Mod.getJunimoNoteInventoryGap)),
                Instructions.Call(typeof(Mod), nameof(Mod.getJunimoNoteInventoryGap)),
                Instructions.Ldc_I4_0()
            );
            FindCode(
                Instructions.Ldc_I4_S(36),
                Instructions.Stfld(typeof(InventoryMenu), nameof(InventoryMenu.capacity))
            )[0] = Instructions.Call(typeof(Mod), nameof(Mod.getJunimoNoteInventoryCapacity));
            
            code = BeginCode();
            for (int i=0; i<2; i++) {
                code = code.FindNext(
                    Instructions.Ldsfld(typeof(IClickableMenu), nameof(IClickableMenu.borderWidth)),
                    OpCodes.Ldc_I4_2,
                    OpCodes.Mul,
                    OpCodes.Add
                );
                code.Remove();
            }
        }
#endregion

#region Assets
        private void OnAssetRequested(object sender, AssetRequestedEventArgs e)
        {
            if (e.Name.IsEquivalentTo("LooseSprites/JunimoNote"))
            {
                e.Edit(asset => {
                    var editor = asset.AsImage();
                    var junimoNote  = Helper.ModContent.Load<IRawTextureData>("JunimoNote.png");
                    // 344,28 - 121x127
                    editor.PatchImage(junimoNote, targetArea: new Rectangle(344, 28, 121, 127));
                });
            }
        }
#endregion

#region Config
        protected override void InitializeApi(IGenericModConfigMenuApi api) {
            api.AddNumberOption(
                mod: ModManifest,
                name: () => "Backpack slots",
                tooltip: () => $"How many slots the premium backpack should have. Must be a multiple of {BackpackSlotsPerRow}, from {MinimumBackpackSlots} to {MaximumBackpackSlots}.",
                getValue: () => getBackpackSlots(),
                setValue: (int val) => config.BackpackSlots = normalizeBackpackSlots(val),
                min: MinimumBackpackSlots,
                max: MaximumBackpackSlots,
                interval: BackpackSlotsPerRow
            );

            api.AddNumberOption(
                mod: ModManifest, 
                name: () => "Backpack cost", 
                tooltip: () => "How much you have to pay to buy this backpack in the shop.", 
                getValue: () => config.BackpackCost, 
                setValue: (int val) => config.BackpackCost = val,
                min: 0
            );
        }
#endregion
    }
}
