using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Playnite.SDK;
using Playnite.SDK.Models;

namespace PlayniteCharts.Interop
{
    /// <summary>
    /// Opens Playnite's *own* game context menu on one of our bubbles.
    ///
    /// The menu is the desktop app's <c>Playnite.DesktopApp.Controls.GameMenu</c>,
    /// built live against the running main view model - not a copy of it. Everything
    /// upstream (or another plugin) puts on the games-list menu turns up here for
    /// free, and nothing here needs touching when they change it.
    ///
    /// The price is reflection. An extension compiles against the SDK only, and the
    /// SDK deliberately does not expose the desktop app - but we are *loaded into*
    /// Playnite.DesktopApp.exe, so the type is right there in the AppDomain. If it
    /// ever is not (the harness, fullscreen mode, an upstream rename) every method
    /// here degrades to "no menu" rather than throwing.
    /// </summary>
    public static class DesktopGameMenu
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        private static bool probed;
        private static Type menuType;
        private static PropertyInfo showStartSection;
        private static PropertyInfo appCurrent;
        private static PropertyInfo mainModelProp;

        /// <summary>
        /// Shows the games-list menu for <paramref name="game"/> at the mouse.
        /// Returns false when the desktop app is not the host, so the caller can
        /// fall back (or, as we do, simply do nothing).
        /// </summary>
        public static bool Show(FrameworkElement target, Game game)
        {
            if (target == null || game == null || !Probe())
            {
                return false;
            }

            try
            {
                var entry = FindEntry(game);
                if (entry == null)
                {
                    return false;
                }

                var menu = Activator.CreateInstance(menuType) as ContextMenu;
                if (menu == null)
                {
                    return false;
                }

                // the games list passes true here; the chart is a game list too
                showStartSection?.SetValue(menu, true);

                // GameMenu reads its games off the DataContext and rebuilds its items
                // every time it opens, so handing it the live entry is all it needs
                menu.DataContext = entry;
                menu.PlacementTarget = target;
                menu.Placement = PlacementMode.MousePoint;
                menu.IsOpen = true;
                return true;
            }
            catch (Exception e)
            {
                logger.Error(e, "Could not open the Playnite game menu.");
                return false;
            }
        }

        /// <summary>
        /// The live <c>GamesCollectionViewEntry</c> for a game. Grouped views hold
        /// one entry per group, so a game can appear more than once - any of them
        /// carries the same Game, and the menu only reads that.
        /// </summary>
        private static object FindEntry(Game game)
        {
            var app = appCurrent?.GetValue(null);
            var model = app == null ? null : mainModelProp?.GetValue(app);
            var view = model?.GetType().GetProperty("GamesView")?.GetValue(model);
            var items = view?.GetType().GetProperty("Items")?.GetValue(view) as IEnumerable;
            if (items == null)
            {
                return null;
            }

            PropertyInfo id = null;
            foreach (var item in items)
            {
                if (item == null)
                {
                    continue;
                }

                if (id == null)
                {
                    id = item.GetType().GetProperty("Id");
                    if (id == null)
                    {
                        return null;
                    }
                }

                if (Equals(id.GetValue(item), game.Id))
                {
                    return item;
                }
            }

            return null;
        }

        private static bool Probe()
        {
            if (probed)
            {
                return menuType != null;
            }

            probed = true;
            try
            {
                var asm = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == "Playnite.DesktopApp");
                if (asm == null)
                {
                    return false;
                }

                var appType = asm.GetType("Playnite.DesktopApp.DesktopApplication");
                appCurrent = appType?.GetProperty("Current", BindingFlags.Public | BindingFlags.Static);
                mainModelProp = appType?.GetProperty("MainModel");
                var type = asm.GetType("Playnite.DesktopApp.Controls.GameMenu");
                if (appCurrent == null || mainModelProp == null || type == null)
                {
                    logger.Warn("Playnite's game menu could not be found; the chart will have no right-click menu.");
                    return false;
                }

                showStartSection = type.GetProperty("ShowStartSection");
                menuType = type;
                return true;
            }
            catch (Exception e)
            {
                logger.Error(e, "Probing for Playnite's game menu failed.");
                return false;
            }
        }
    }
}
