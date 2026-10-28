using System.Diagnostics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using PristonTale.Gameplay.Screens.Framework;
using PristonTale.Gameplay.UI.Chat;
using PristonTale.Gameplay.UI.Inventory;
using PristonTale.Gameplay.UI.Npc;
using PristonTale.Gameplay.UI.Quests;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Gameplay.UI.Travel;
using PristonTale.Gameplay.UI.Widgets;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Progression;
using PristonTale.Simulation.Quests;
using PristonTale.Simulation.World.Npcs;
using PristonTale.Contracts.Characters;

namespace PristonTale.Gameplay.UI;

// NPC services: opening their windows, the shop, warehouse and workbench actions, item drops from the bag and the icons they need.
public sealed partial class GameplayHud
{
    // A service the server opened shows its window; the NPC dialog gives way to it, as the original opens services directly.
    private void ApplyServices(OwnerState state)
    {
        if (_services.Opened != _serviceOpened)
        {
            _serviceOpened = _services.Opened;
            OpenService();
        }

        if (_services.Open != NpcServiceKind.Shop && _shop.IsOpen)
        {
            _shop.Hide();
        }

        if (_services.Open != NpcServiceKind.Warehouse && _warehouse.IsOpen)
        {
            _warehouse.Hide();
        }

        if (_services.Open != NpcServiceKind.ItemAging && _aging.IsOpen)
        {
            _aging.Hide();
        }

        _aging.Sync(state);
        if (_services.Open != NpcServiceKind.ItemMix && _mix.IsOpen)
        {
            _mix.Hide();
        }

        _mix.Sync(state);

        if (_warehouse.IsOpen && _services.Warehouse is { } view && !ReferenceEquals(view, _warehouse.View))
        {
            _warehouse.Refresh(view);
            RequestIcons(_state);
        }

        if (!_services.Pending)
        {
            _services.RefreshQuotes(state);
        }
    }

    // The icon cache keeps only the last requested set, so every request names everything on screen: the pack, the open
    // shop's stock and the open warehouse.
    private void RequestIcons(OwnerState state)
    {
        RememberKeys(state?.Items);
        RememberKeys(_warehouse.View?.Items);
        var keys = state?.Items.Select(static item => item.CatalogKey) ?? [];
        if (_shop.IsOpen)
        {
            keys = keys.Concat(_shop.IconKeys);
        }

        if (_warehouse.IsOpen && _warehouse.View is { } view)
        {
            keys = keys.Concat(view.Items.Select(static item => item.CatalogKey));
        }

        _icons.Request(keys);
    }

    // Keys of the items seen in the pack or the warehouse, so a sold or stored item still has its sound once it is gone.
    private void RememberKeys(IEnumerable<OwnedItemView> items)
    {
        if (_itemKeys.Count > 4096)
        {
            _itemKeys.Clear();
        }

        foreach (var item in items ?? [])
        {
            _itemKeys[item.InstanceId] = item.CatalogKey;
        }
    }

    private void ServiceSound(NpcServiceCommand command)
    {
        var key = command.Action == NpcServiceAction.Buy
            ? ItemCodes.TryFormat(command.ItemCode, out var bought) ? bought : null
            : command.Action is NpcServiceAction.Sell or NpcServiceAction.Deposit or NpcServiceAction.Withdraw or NpcServiceAction.Move
                ? _itemKeys.GetValueOrDefault(command.ItemId)
                : null;
        if (key is not null)
        {
            ItemSound?.Invoke(key);
        }
    }

    private void OpenService()
    {
        switch (_services.Open)
        {
            case NpcServiceKind.Shop:
                _npcOpen = false;
                _shop.Show(_services.Catalog);
                RequestIcons(_state);
                OpenOnly(_inventorySlide);
                break;
            case NpcServiceKind.Warehouse when _services.Warehouse is { } view:
                _npcOpen = false;
                _warehouse.Show(view);
                RequestIcons(_state);
                OpenOnly(_inventorySlide);
                break;
            case NpcServiceKind.Teleporter:
                _npc.ShowDestinations(_services.Destinations);
                break;
            case NpcServiceKind.ItemAging:
                _npcOpen = false;
                _aging.Show(_npc.Current?.Services.FirstOrDefault(static offer => offer.Kind == NpcServiceKind.ItemAging)?.PricePercent ?? 100);
                RequestIcons(_state);
                OpenOnly(_inventorySlide);
                break;
            case NpcServiceKind.ItemMix:
            {
                _npcOpen = false;
                var offer = _npc.Current?.Services.FirstOrDefault(static offer => offer.Kind == NpcServiceKind.ItemMix);
                _mix.Show(offer?.Value == Simulation.Items.Mixing.MixingWork.AlchemyMode, offer?.PricePercent ?? 100);
                RequestIcons(_state);
                OpenOnly(_inventorySlide);
                break;
            }
            case NpcServiceKind.QuestEvent:
                if (_puzzle.Offer(_state, _services.NpcId, _services.NpcName) is { } help)
                {
                    _notice.Show(help);
                }
                else
                {
                    _npcOpen = false;
                    _npc.Dismiss();
                    _inventorySlide.Open = false;
                }

                break;
            case NpcServiceKind.SkillMaster:
                _npcOpen = false;
                _skillMaster = _services.NpcId;
                OpenOnly(_skillSlide);
                break;
        }
    }

    private void CloseServices()
    {
        _services.Close();
        foreach (var window in _serviceWindows)
        {
            window.Hide();
        }
    }

    private void ClickShop(OwnerState state, Point pointer)
    {
        switch (_shop.Click(pointer))
        {
            case HudShopAction.Exit:
                CloseServices();
                break;
            case HudShopAction.Buy when _shop.Selected is { } offer && state is not null:
                _services.Request(_services.NpcId, _services.NpcName, NpcServiceKind.Shop, NpcServiceAction.Buy, state.InventoryRevision, offer.Code);
                break;
            case HudShopAction.RepairAll when state is not null:
                _services.Request(_services.NpcId, _services.NpcName, NpcServiceKind.Shop, NpcServiceAction.RepairAll, state.InventoryRevision);
                break;
        }
    }

    private bool InterceptInventoryClick(OwnedItemView item)
    {
        if (_aging.Holds(item.InstanceId) || _mix.Holds(item.InstanceId))
        {
            return true;
        }

        if (!_shop.FullyOpen || _shop.Mode == HudShopMode.Buy || _state is null)
        {
            return false;
        }

        if (_shop.Mode == HudShopMode.Sell && item.Slot >= 0)
        {
            return true;
        }

        var action = _shop.Mode == HudShopMode.Sell ? NpcServiceAction.Sell : NpcServiceAction.Repair;
        _services.Request(_services.NpcId, _services.NpcName, NpcServiceKind.Shop, action, _state.InventoryRevision, itemId: item.InstanceId);
        return true;
    }

    // An inventory item dropped on the shop is sold; on the warehouse grid it is stored in the cell under the pointer.
    private bool DropFromInventory(Guid itemId, Point pointer)
    {
        if (_state is null)
        {
            return false;
        }

        if (_shop.FullyOpen && _shop.Contains(pointer))
        {
            _services.Request(_services.NpcId, _services.NpcName, NpcServiceKind.Shop, NpcServiceAction.Sell, _state.InventoryRevision, itemId: itemId);
            return true;
        }

        var dropped = _state.Items.FirstOrDefault(item => item.InstanceId == itemId);
        if ((_aging.FullyOpen ? _aging.Place(dropped, pointer, _state) : _mix.FullyOpen ? _mix.Place(dropped, pointer, _state) : null) is { } refusal)
        {
            if (refusal.Length > 0)
            {
                _notice.Show(refusal);
            }

            return true;
        }

        var cell = HudStoreLayout.CellAt(_warehouse.Origin, pointer);
        if (_warehouse.FullyOpen && cell >= 0)
        {
            _services.Request(_services.NpcId, _services.NpcName, NpcServiceKind.Warehouse, NpcServiceAction.Deposit, _state.InventoryRevision,
                itemId: itemId, anchor: cell);
            return true;
        }

        return false;
    }

    private void ApplyWarehouseAction(OwnerState state, HudWarehouseAction action)
    {
        var revision = state?.InventoryRevision ?? 0;
        switch (action.Kind)
        {
            case HudWarehouseActionKind.Exit:
                CloseServices();
                break;
            case HudWarehouseActionKind.Move:
                _services.Request(_services.NpcId, _services.NpcName, NpcServiceKind.Warehouse, NpcServiceAction.Move, revision,
                    itemId: action.ItemId, anchor: action.Anchor);
                break;
            case HudWarehouseActionKind.Withdraw:
                _services.Request(_services.NpcId, _services.NpcName, NpcServiceKind.Warehouse, NpcServiceAction.Withdraw, revision,
                    itemId: action.ItemId, anchor: action.Anchor);
                break;
        }
    }

    private void ApplyWorkAction(HudWorkbench bench, OwnerState state, HudWorkActionKind action)
    {
        switch (action)
        {
            case HudWorkActionKind.Exit:
                CloseServices();
                break;
            case HudWorkActionKind.Start when state is not null:
                if (state.Money < bench.Price(state))
                {
                    _notice.Show("Not enough gold");
                    break;
                }

                bench.Started(state);
                _services.Request(_services.NpcId, _services.NpcName, bench.Service, bench.WorkAction, state.InventoryRevision, itemId: bench.Item,
                    materials: bench.Materials);
                break;
        }
    }

    // The result of the work the bench sent last, once it arrived; a refusal without a result ends the craft as failed.
    private void ShowWorkResult(HudWorkbench bench)
    {
        if (_services.LastCommand is not { } command || command.Action != bench.WorkAction)
        {
            return;
        }

        if (_services.LastWork is not { } work)
        {
            if (!_services.LastOk)
            {
                CraftResult?.Invoke(false);
            }

            return;
        }

        _notice.Show(bench.ResultText(work.Result));
        CraftResult?.Invoke(bench.Succeeded(work.Result));
        CloseServices();
    }

    private int InventoryCellAt(Point pointer)
    {
        if (!_inventorySlide.FullyOpen)
        {
            return -1;
        }

        return HudInventoryLayout.CellAt(PanelOrigin(Layout(), _inventorySlide), pointer);
    }

    private string InventoryNote(OwnedItemView item)
    {
        if (!_shop.FullyOpen || _services.QuoteFor(item.InstanceId) is not { } quote)
        {
            return "";
        }

        if (_shop.Mode == HudShopMode.Repair)
        {
            return quote.RepairCost > 0 ? $"Repair: {quote.RepairCost:N0} gold" : "Not damaged";
        }

        return quote.SellPrice >= 0 ? $"Sells for {quote.SellPrice:N0} gold" : "The shop does not buy this";
    }

    private Task<PristonTale.Simulation.Items.Usable.ItemUseOutcome> UseItemAsync(PristonTale.Simulation.Items.Usable.ItemUseCommand command) =>
        UseItem is { } use ? use(command)
            : Task.FromResult(PristonTale.Simulation.Items.Usable.ItemUseOutcome.Refused(command.ExpectedRevision,
                PristonTale.Simulation.Items.Usable.ItemUseStatus.NotUsable));
}
