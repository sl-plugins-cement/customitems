# CustomItems

<p align="center">
  <a href="#english"><strong>English</strong></a>
  <span> | </span>
  <a href="#chinese"><strong>中文</strong></a>
</p>

## English

CustomItems is a source-shared LabAPI helper library for SCP: Secret Laboratory item plugins. It is not a standalone LabAPI plugin: it does not register commands, create config files, show player text, or run by itself. Item plugins compile its sources into their own plugin assembly to reuse common custom-item mechanics instead of copying the same tracking and held-model code into every plugin. The library holds no process-wide state (`ItemRegistry` and `HeldMeshManager` are per-plugin instances), so each consumer carrying its own copy is the intended layout. It has no dependency on any other plugin or shared DLL.

### What It Provides

- `ItemRegistry<TKind>`: a lightweight serial-number registry that maps vanilla item or pickup serials to a plugin-owned enum. API 2 adds an attributable lifecycle contract for grant → track → transfer/drop → destroy/untrack while retaining every API 1 method signature.
- `HeldMeshManager`: lifecycle management for camera-tracked first-person custom meshes. It can show a mesh over the native viewmodel or hide the native viewmodel and replace it.
- `HeldMeshSpec`, `HeldLightSpec`, and `MeshPrimitive`: neutral data objects for describing AdminToy primitive meshes and optional pulsing core lights. A `MeshPrimitive` may name another primitive as its `ParentName`, which spawns it as a CHILD of that primitive with its authored local pose. That is what makes SHEAR RIGS possible: an exact parallelogram needs a non-uniformly scaled (usually invisible) parent plus a rotated child, which no single AdminToy can express, because pos+rot+scale composes to R*S and never shear. Parented primitives are left out of the mesh-centre bounding box, since their coordinates are in the parent's frame.
- `HeldVisualMode`: the display mode for held meshes: `None`, `Overlay`, or `HideAndReplace`.
- `HeldMeshVisual`: the low-level spawned visual implementation. Most plugins should use `HeldMeshManager` rather than constructing this directly.
- `HeldMeshWorldSpec` and `HeldMeshPresentation`: optional physical-size observer companions and plugin-owned FP/world audience callbacks. Set `preserveAuthoredOrigin` to keep a model's grip origin and apply its explicit root rotation. Existing constructors retain their previous defaults.
- `PlayerModelAttachment`: an invisible neutral parent that cancels player scale before rotated model roots. The caller owns its update and cleanup; it creates no independent loop or event subscription.

### Installation

CustomItems is consumed as source. Consumers do not deploy `CustomItems.dll`; they compile `src\**\*.cs` into their own plugin assembly by adding this to their csproj:

```xml
<PropertyGroup>
  <!-- CustomItems is compiled from source. Default: the sibling metarepo checkout; a task worktree under
       .worktrees/<task>/<repo> resolves three levels up; anything else passes -p:CustomItemsSource=<path>. -->
  <CustomItemsSource Condition="'$(CustomItemsSource)' == '' And Exists('$(MSBuildThisFileDirectory)..\CustomItems\src')">$(MSBuildThisFileDirectory)..\CustomItems\src</CustomItemsSource>
  <CustomItemsSource Condition="'$(CustomItemsSource)' == '' And Exists('$(MSBuildThisFileDirectory)..\..\..\CustomItems\src')">$(MSBuildThisFileDirectory)..\..\..\CustomItems\src</CustomItemsSource>
</PropertyGroup>
<ItemGroup>
  <Compile Include="$(CustomItemsSource)\**\*.cs" Link="CustomItems\%(RecursiveDir)%(Filename)%(Extension)" />
</ItemGroup>
<Target Name="RequireCustomItemsSource" BeforeTargets="BeforeBuild" Condition="!Exists('$(CustomItemsSource)')">
  <Error Text="CustomItems sources not found. Pass -p:CustomItemsSource=&lt;path to CustomItems\src&gt;." />
</Target>
```

The consuming project must already reference the SCP:SL managed assemblies these sources use (`Assembly-CSharp`, `Assembly-CSharp-firstpass`, `LabApi`, `Mirror`, `UnityEngine`, `UnityEngine.CoreModule`); a normal LabAPI plugin project does. The consuming plugin is then deployed normally under:

```text
%APPDATA%\SCP Secret Laboratory\LabAPI\plugins\<active port>\
```

An old `CustomItems.dll` left in a LabAPI dependencies folder is harmless but no longer needed.

`CustomItems.csproj` exists so the library still builds and is checked standalone:

```powershell
dotnet build .\CustomItems.csproj
```

The project targets `net48` and expects SCP:SL managed assemblies under:

```text
C:\Program Files (x86)\Steam\steamapps\common\SCP Secret Laboratory Dedicated Server\SCPSL_Data\Managed
```

Override that location when needed:

```powershell
dotnet build .\CustomItems.csproj -p:SCP_SL_MANAGED="D:\Servers\SCPSL\SCPSL_Data\Managed"
```

### Usage Pattern

For serial tracking, create one registry per plugin or per item domain:

```csharp
private enum SpecialItemKind
{
    AnchorTool,
    MedicTool,
}

private readonly ItemRegistry<SpecialItemKind> _items = new();
```

Mark the serial when your plugin grants, spawns, or identifies a custom item. The API 1 call remains supported:

```csharp
_items.Mark(item, SpecialItemKind.AnchorTool);
```

For lifecycle diagnostics, use the API 2 methods and supply a short source name. `TrackGranted` emits
`Grant` then `Track`; transfer and drop preserve the serial identity; `Destroy` emits `Destroy` then
`Untrack`. Ordinary `Untrack` is for retirement/consumption where no world pickup was destroyed.

```csharp
_items.TrackGranted(item.Serial, SpecialItemKind.AnchorTool, player.UserId, "loadout");
_items.Drop(item.Serial, player.UserId, "player-drop");
_items.Transfer(item.Serial, newHolder.UserId, "pickup");
_items.Destroy(item.Serial, "world-pickup-destroyed");
```

Set `TraceLifecycle = true` only for diagnostics; it emits one DEBUG line containing stage, serial, kind,
from/to UserIds, and source. Structured consumers can subscribe to `LifecycleChanged` instead. The shared
contract records identity and ownership—it deliberately does not impose gameplay transition validation.

Gate event handlers by serial:

```csharp
if (!_items.IsKind(args.Item.Serial, SpecialItemKind.AnchorTool))
{
    return;
}
```

Call `Clear()` on round reset and plugin disable so serials never leak across rounds.

For held custom meshes, build a `HeldMeshSpec` from your plugin's loaded primitives, then own one `HeldMeshManager` in the service that manages the item:

```csharp
private readonly HeldMeshManager _heldMesh = new("MyItem");

_heldMesh.Show(player, spec, HeldVisualMode.HideAndReplace);
```

If `HideAndReplace` is used, the manager forces `player.CurrentItem = null` to suppress the native viewmodel. That creates a `ChangedItem(None)` event. In your changed-item handler, call `AbsorbForcedNone(player)` before treating the deselect as a real unequip:

```csharp
if (args.NewItem == null && _heldMesh.AbsorbForcedNone(args.Player))
{
    return;
}
```

Call `Hide(player)` on real unequip, death, disconnect, or role change. Call `Clear()` on round reset and plugin disable.

Canonical visuals (`PreserveAuthoredOrigin`) bind the exact selected native item before force-deselect.
Removing that item, including `Player.ClearInventory`, destroys both held representations even when
there is no further `ChangedItem` event. The manager also clears its visual and deselect bookkeeping.
Direct `HeldMeshVisual` owners can use `CarrierRemoved` to clear their armed/channel state and call
`BindCurrentItem()` when reusing a visual for a newly selected carrier. The subscription is released
on every visual teardown; legacy specs keep their existing behavior. This uses the public
`ItemBase.OnItemRemoved` event from `InventorySystem/Items/ItemBase.cs:OnDestroy`, also subscribed by
the official LabAPI wrapper in `LabApi/Features/Wrappers/Items/Item.cs:Initialize`.

### Commands And Config

CustomItems has no Remote Admin commands, player-console commands, Server-Specific Settings, or config file. Commands, permissions, localization, hints, and config remain the responsibility of the plugin that references this library.

### Known Limitations

- The library only supplies shared mechanics. It does not spawn or grant custom items by itself.
- Held meshes are server-side AdminToy primitives and lights. Keep primitive counts modest and avoid repeatedly spawning or destroying meshes in high-frequency code.
- `HideAndReplace` intentionally manipulates the current item to hide the native first-person viewmodel. Plugin code must absorb the manager's forced `ChangedItem(None)` event or it may incorrectly disarm the custom item.
- Callers still own unequip, reset and disable cleanup through `Hide()`/`Clear()`. Canonical models additionally bind their exact native carrier before deselection, so native inventory removal destroys the art even when the selected slot is already empty.
- Client-side UI, inventory names, and native item icons are not changed by this library.

## Chinese

CustomItems 是一个以源码形式共享的 SCP: Secret Laboratory 物品插件 LabAPI 辅助库。它不是独立的 LabAPI 插件：不会注册命令、不会生成配置文件、不会显示玩家文本，也不会单独运行。物品插件把它的源码编译进自己的插件程序集，用来复用常见的自定义物品追踪和手持模型逻辑，避免每个插件都复制一份实现。本库不持有任何进程级状态（`ItemRegistry` 与 `HeldMeshManager` 都是插件各自的实例），因此每个消费者自带一份副本正是预期布局。它不依赖任何其他插件或共享 DLL。

### 提供的功能

- `ItemRegistry<TKind>`：轻量序列号注册表，把原版物品或拾取物的 serial 映射到插件自己的枚举。API 2 新增可归因的“发放 → 追踪 → 转移/丢弃 → 销毁/取消追踪”生命周期契约，并保留全部 API 1 方法签名。
- `HeldMeshManager`：管理跟随摄像机的第一人称自定义手持网格生命周期。可以把模型叠加在原版手持模型上，也可以隐藏原版手持模型并替换成自定义模型。
- `HeldMeshSpec`、`HeldLightSpec`、`MeshPrimitive`：用于描述 AdminToy primitive 网格和可选脉冲核心光源的中立数据对象。`MeshPrimitive` 可通过 `ParentName` 指定同一网格中的另一个图元作为父级，从而以其原始局部姿态作为子对象生成。这正是**剪切装配**得以实现的前提：精确的平行四边形需要一个非等比缩放的（通常不可见的）父级加一个旋转的子级，而单个 AdminToy 无法表达——位置+旋转+缩放只能合成 R*S，永远不含剪切。带父级的图元不会参与网格包围盒中心的计算，因为其坐标位于父级坐标系中。
- `HeldVisualMode`：手持模型显示模式：`None`、`Overlay`、`HideAndReplace`。
- `HeldMeshVisual`：底层已生成视觉对象实现。大多数插件应使用 `HeldMeshManager`，不要直接构造它。
- `HeldMeshWorldSpec` 与 `HeldMeshPresentation`：可选的实际尺寸他人持握模型，以及由插件管理的第一人称/世界模型可见性回调。设置 `preserveAuthoredOrigin` 可保留握点原点并应用明确的根节点旋转；现有构造函数维持原来的默认行为。
- `PlayerModelAttachment`：不可见的中性父节点，在模型根节点旋转前抵消玩家缩放。更新与清理由调用方负责，不创建独立循环或事件订阅。

### 安装

CustomItems 以源码形式使用。消费插件不部署 `CustomItems.dll`，而是在自己的 csproj 中加入以下内容，把 `src\**\*.cs` 编译进自己的插件程序集：

```xml
<PropertyGroup>
  <!-- CustomItems is compiled from source. Default: the sibling metarepo checkout; a task worktree under
       .worktrees/<task>/<repo> resolves three levels up; anything else passes -p:CustomItemsSource=<path>. -->
  <CustomItemsSource Condition="'$(CustomItemsSource)' == '' And Exists('$(MSBuildThisFileDirectory)..\CustomItems\src')">$(MSBuildThisFileDirectory)..\CustomItems\src</CustomItemsSource>
  <CustomItemsSource Condition="'$(CustomItemsSource)' == '' And Exists('$(MSBuildThisFileDirectory)..\..\..\CustomItems\src')">$(MSBuildThisFileDirectory)..\..\..\CustomItems\src</CustomItemsSource>
</PropertyGroup>
<ItemGroup>
  <Compile Include="$(CustomItemsSource)\**\*.cs" Link="CustomItems\%(RecursiveDir)%(Filename)%(Extension)" />
</ItemGroup>
<Target Name="RequireCustomItemsSource" BeforeTargets="BeforeBuild" Condition="!Exists('$(CustomItemsSource)')">
  <Error Text="CustomItems sources not found. Pass -p:CustomItemsSource=&lt;path to CustomItems\src&gt;." />
</Target>
```

消费项目需要已经引用这些源码用到的 SCP:SL 托管程序集（`Assembly-CSharp`、`Assembly-CSharp-firstpass`、`LabApi`、`Mirror`、`UnityEngine`、`UnityEngine.CoreModule`）；普通的 LabAPI 插件项目都已具备。随后消费插件正常部署到：

```text
%APPDATA%\SCP Secret Laboratory\LabAPI\plugins\<active port>\
```

LabAPI 依赖目录中遗留的旧 `CustomItems.dll` 无害，但已不再需要。

`CustomItems.csproj` 的存在是为了让本库仍可独立构建和检查：

```powershell
dotnet build .\CustomItems.csproj
```

项目目标框架为 `net48`，默认从以下位置读取 SCP:SL 托管程序集：

```text
C:\Program Files (x86)\Steam\steamapps\common\SCP Secret Laboratory Dedicated Server\SCPSL_Data\Managed
```

需要时可以覆盖路径：

```powershell
dotnet build .\CustomItems.csproj -p:SCP_SL_MANAGED="D:\Servers\SCPSL\SCPSL_Data\Managed"
```

### 使用方式

序列号追踪建议每个插件或每个物品域创建一个注册表：

```csharp
private enum SpecialItemKind
{
    AnchorTool,
    MedicTool,
}

private readonly ItemRegistry<SpecialItemKind> _items = new();
```

当插件发放、生成或识别自定义物品时标记 serial。API 1 调用仍然受支持：

```csharp
_items.Mark(item, SpecialItemKind.AnchorTool);
```

需要生命周期诊断时，使用 API 2 方法并传入简短来源名。`TrackGranted` 依次发出 `Grant` 与 `Track`；转移和丢弃保留同一 serial；`Destroy` 依次发出 `Destroy` 与 `Untrack`。没有世界拾取物销毁的退役/消耗路径使用普通 `Untrack`。

```csharp
_items.TrackGranted(item.Serial, SpecialItemKind.AnchorTool, player.UserId, "loadout");
_items.Drop(item.Serial, player.UserId, "player-drop");
_items.Transfer(item.Serial, newHolder.UserId, "pickup");
_items.Destroy(item.Serial, "world-pickup-destroyed");
```

仅在诊断时启用 `TraceLifecycle = true`；每次转换会输出包含阶段、serial、kind、来源/目标 UserId 与来源名的 DEBUG 行。结构化诊断也可订阅 `LifecycleChanged`。共享契约只记录身份与持有者，不会额外施加游戏逻辑状态校验。

在事件处理器中按 serial 过滤：

```csharp
if (!_items.IsKind(args.Item.Serial, SpecialItemKind.AnchorTool))
{
    return;
}
```

在回合重置和插件禁用时调用 `Clear()`，避免 serial 跨回合残留。

自定义手持网格需要先把插件加载的 primitive 转成 `HeldMeshSpec`，然后在管理该物品的服务里持有一个 `HeldMeshManager`：

```csharp
private readonly HeldMeshManager _heldMesh = new("MyItem");

_heldMesh.Show(player, spec, HeldVisualMode.HideAndReplace);
```

如果使用 `HideAndReplace`，管理器会通过 `player.CurrentItem = null` 隐藏原版第一人称手持模型。这会触发一次 `ChangedItem(None)` 事件。在你的切换物品事件处理器里，应先调用 `AbsorbForcedNone(player)`，再把这次取消手持当作真正的卸下：

```csharp
if (args.NewItem == null && _heldMesh.AbsorbForcedNone(args.Player))
{
    return;
}
```

真正卸下、死亡、断线或切换角色时调用 `Hide(player)`。回合重置和插件禁用时调用 `Clear()`。

标准模型（`PreserveAuthoredOrigin`）在强制取消手持前绑定当前选中的原生物品实例。该物品被移除时，
包括执行 `Player.ClearInventory`，即使没有再次触发 `ChangedItem`，两种手持外观也会销毁；管理器同时
清除模型记录及强制取消手持标记。直接管理 `HeldMeshVisual` 的服务可订阅 `CarrierRemoved` 来清除武装
或引导状态，并在复用模型显示新选中物品时调用 `BindCurrentItem()`。每次模型销毁都会取消事件订阅，
旧规格保留原有行为。此实现使用 `InventorySystem/Items/ItemBase.cs:OnDestroy` 中的公开
`ItemBase.OnItemRemoved` 事件；官方 LabAPI 也在 `LabApi/Features/Wrappers/Items/Item.cs:Initialize`
中订阅该事件维护物品包装器。

### 命令与配置

CustomItems 没有 Remote Admin 命令、玩家控制台命令、Server-Specific Settings 或配置文件。命令、权限、本地化、提示和配置都由引用此库的具体插件负责。

### 已知限制

- 本库只提供共享机制，本身不会生成或发放自定义物品。
- 手持网格使用服务端 AdminToy primitives 和 light。primitive 数量应保持适中，避免在高频逻辑中反复生成或销毁模型。
- `HideAndReplace` 会刻意修改当前物品来隐藏原版第一人称模型。插件代码必须吸收管理器触发的 `ChangedItem(None)`，否则可能误判为玩家真正卸下了自定义物品。
- 调用方仍需通过 `Hide()`/`Clear()` 处理卸下、重置与禁用。标准模型还会在取消选中前绑定准确的原生承载物，因此即使当前栏位已为空，原生背包移除事件也会销毁附加模型。
- 本库不会修改客户端 UI、背包物品名称或原版物品图标。
