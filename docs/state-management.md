# Blazor Server State Management — Reference

## Overview

This app uses Blazor Server, where all rendering runs on the server over a persistent SignalR connection (a "circuit") per browser tab. State shared between pages within a single circuit uses scoped DI state-container services. State that must survive a direct URL hit, page refresh, or be shareable as a link must be encoded in route or query parameters and re-resolved from a data source — DI state alone cannot guarantee population on cold navigation. Never register per-user state as Singleton.

---

## Decision table

| Scenario | Recommended approach | Notes |
|---|---|---|
| Simple state handoff between two pages in the same session, no live reactivity needed | Scoped DI state-container service (`SelectedCase` set before `NavigateTo`) | Simplest path. No subscription required if only one component reads the value. |
| Multiple components need to react live when shared state changes | Scoped DI state-container service with `event Action? OnChange` | Subscribers must implement `IDisposable` and unsubscribe in `Dispose`. |
| State must survive a direct URL hit, page refresh, or be shareable as a link | Route or query parameters + data-resolution fallback (e.g., `CaseRepository.GetById`) | DI state is circuit-scoped and starts empty on every new circuit. Always handle the null/missing case. |
| Multi-step form/wizard where data is newly authored by the user and must survive forward/backward navigation across steps in the same session | Scoped DI draft state-container service with a `Clear()` method (e.g., `OrderDraftState`) | No repository fallback exists — the data doesn't exist anywhere until the user confirms. Missing draft state means redirect to the start of the flow, not a lookup. Each step reads the existing draft and merges its own fields using `with` before navigating forward; earlier steps must preserve fields set by later steps when the user navigates back. |
| User input in a child component needs to reach the parent within the same page — no navigation, no DI service | `[Parameter] EventCallback<T>` on the child; parent wires the handler inline or with `@bind-Value` sugar | The child stays stateless/presentation-only. The parent is the single source of truth. Use `@bind-Value` (requires `ValueChanged` companion parameter) or wire the callback explicitly. |
| A deeply nested descendant needs a value from a distant ancestor, and requiring every intermediate component to explicitly pass that value along (via parameters or EventCallback) would pollute components that have no real relationship to the value | `CascadingValue` / `[CascadingParameter]` — the ancestor provides the value once, descendants declare `[CascadingParameter]` to receive it directly, and intermediate layers are completely unaware | Use plain `CascadingValue` when descendants only need the initial value. Use `CascadingValueSource` with `isFixed: false` and `NotifyChangedAsync()` when descendants need to react live to changes after initial render. |
| State must survive a full browser restart or be available offline | Browser storage (`localStorage`/`sessionStorage`) via JS interop or `ProtectedBrowserStorage` | Outside the scope of this PoC. See Microsoft docs for `ProtectedLocalStorage`. |
| Per-user state registered as Singleton *(anti-pattern)* | **Never do this.** Use Scoped instead. | Singleton is shared across all users on the server process. State leaks between users' sessions. |

---

## Pattern: Scoped state-container service

**Canonical example:** [Services/CaseSelectionState.cs](../Services/CaseSelectionState.cs)

The pattern:

```csharp
public class CaseSelectionState
{
    private CaseDto? _selectedCase;

    public event Action? OnChange;

    public CaseDto? SelectedCase
    {
        get => _selectedCase;
        set { _selectedCase = value; OnChange?.Invoke(); }
    }
}
```

- Register as **Scoped** in `Program.cs`: one instance per SignalR circuit (one per browser tab).
- The setter raises `OnChange` so any subscribed component re-renders automatically.
- A writer (e.g., CaseList) sets the property before navigating; it does not subscribe.
- A reader that needs live updates (e.g., CaseDetail) subscribes in `OnInitialized` and **must** unsubscribe in `Dispose`.

**Rule:** Any component that subscribes to a shared service's `OnChange` event **must** implement `IDisposable` and unsubscribe in `Dispose`. Failing to do so causes memory leaks and stale event handlers that fire on disposed components, which can trigger `ObjectDisposedException` or ghost re-renders.

```razor
@implements IDisposable

@code {
    protected override void OnInitialized()
    {
        CaseSelectionState.OnChange += StateHasChanged;
    }

    public void Dispose()
    {
        CaseSelectionState.OnChange -= StateHasChanged;
    }
}
```

---

## Pattern: Route/query parameter state

**Canonical example:** [Components/Pages/CaseDetail.razor](../Components/Pages/CaseDetail.razor) + [Services/CaseRepository.cs](../Services/CaseRepository.cs)

Route parameters (`{Id:int}`) are the only state that survives a direct URL hit, refresh, or shared link. `OnParametersSet` is the correct lifecycle hook for reacting to them — it fires on every parameter update, including the initial render.

The two-path resolution pattern:

```csharp
protected override void OnParametersSet()
{
    if (CaseSelectionState.SelectedCase is { } c && c.Id == Id)
    {
        // Fast path: state already populated (normal in-session navigation)
        _case = c;
    }
    else
    {
        // Fallback: cold navigation (direct URL, refresh) — resolve from data source
        _case = CaseRepository.GetById(Id);
        if (_case is not null)
            CaseSelectionState.SelectedCase = _case; // sync state for any other subscribers
    }
}
```

**Rule:** Any page reachable via direct URL must handle the case where DI-held state is absent. Never assume `CaseSelectionState.SelectedCase` (or equivalent) is populated on arrival. Provide either a repository/data-resolution fallback or an explicit "not found" state — never render partial or undefined UI.

---

## Pattern: Draft/wizard state container

**Canonical example:** [Services/OrderDraftState.cs](../Services/OrderDraftState.cs) + [Components/Pages/OrderNew.razor](../Components/Pages/OrderNew.razor) + [Components/Pages/OrderContact.razor](../Components/Pages/OrderContact.razor) + [Components/Pages/OrderReview.razor](../Components/Pages/OrderReview.razor)

This pattern applies when data is being **newly authored** by the user across multiple steps — it does not exist anywhere outside the user's current input. It is distinct from the scoped state-container pattern above, where the data being passed already exists in a repository and can be re-fetched as a fallback.

The order wizard is a three-step flow:

```
/order/new  (Step 1: Name + Description)
    ↓
/order/new/contact  (Step 2: Email + Phone)
    ↓
/order/new/review  (Step 3: read-only summary + confirm)
```

A single `OrderDraftState` instance (scoped to the circuit) holds the entire in-progress `OrderDto` across all three steps. Each intermediate step updates the draft with its own fields using the `with` expression (record non-destructive mutation) before navigating forward:

```csharp
// OrderContact.razor — Step 2 merges its fields into the existing draft
OrderDraftState.Draft = OrderDraftState.Draft! with { Email = _email, Phone = _phone };
NavigationManager.NavigateTo("/order/new/review");
```

Step 1 (`OrderNew.razor`) preserves any email/phone already entered in Step 2 when the user navigates back and then forward again, avoiding overwrite:

```csharp
var existing = OrderDraftState.Draft;
OrderDraftState.Draft = new OrderDto(_name, _description, existing?.Email ?? string.Empty, existing?.Phone ?? string.Empty);
```

Key distinction: because the data has no independent existence, there is **no repository or fallback-resolution option** when the draft state is missing. A user arriving at any step beyond Step 1 with no in-progress draft (direct URL, refresh, expired circuit) cannot have their data recovered — the only correct response is to redirect them back to the start of the flow.

```csharp
protected override void OnInitialized()
{
    if (RendererInfo.IsInteractive && OrderDraftState.Draft is null)
        NavigationManager.NavigateTo("/order/new");
}
```

Guard the markup with a null check (`@if (OrderDraftState.Draft is not null)`) so no content renders before the redirect fires.

**Rule: call `Clear()` on confirm, not on back.** "Back to edit" and "Confirm" both navigate away from the review step, but they have different state lifecycle implications:

- **Back to edit** — must preserve the draft so the form page can re-populate from it. Do not call `Clear()`.
- **Confirm/complete** — the draft's lifecycle is over. Call `Clear()` before navigating away so stale in-progress data doesn't linger in the circuit after the flow is done.

**Rule:** Any service holding in-progress, not-yet-committed user input must expose a `Clear()` method (or equivalent). Callers are responsible for calling it at the correct transition point — omitting it means the draft survives indefinitely within the circuit, which can cause a later visit to the same wizard to appear pre-populated with stale data from a previous run.

---

## Pattern: Child-to-parent communication

**Canonical examples:** [Components/NameInput.razor](../Components/NameInput.razor) + [Components/DescriptionInput.razor](../Components/DescriptionInput.razor), both used in [Components/Pages/OrderNew.razor](../Components/Pages/OrderNew.razor)

This pattern is categorically different from the DI-service-based patterns above. It involves no scoped service, no navigation boundary, and no circuit-level state container. It is plain Blazor component composition: the same `[Parameter]` mechanism used for parent-to-child data flow, just flowing the other direction via `EventCallback<T>`.

The two child components in this repo demonstrate the same underlying mechanism at different levels of syntactic sugar:

- **`NameInput.razor`** — uses `@bind-Value` on the parent side. This requires the child to expose a `[Parameter] string Value` and a companion `[Parameter] EventCallback<string> ValueChanged` (exact name required). Blazor desugars `@bind-Value="_name"` into `Value="@_name" ValueChanged="v => _name = v"` at compile time. The mechanism is identical to the explicit form; the sugar hides it.

- **`DescriptionInput.razor`** — uses an explicit, manually-wired callback. The parameter is named `OnValueChanged` (not `ValueChanged`), which deliberately breaks the `@bind-Value` convention, forcing the parent to write the wiring in full: `Value="@_description" OnValueChanged="v => _description = v"`. This makes the underlying dispatch-and-assign loop visible.

Both approaches produce the same rendered HTML and the same runtime behavior. The choice between them is ergonomic, not architectural.

```razor
@* NameInput.razor — supports @bind-Value sugar *@
<input value="@Value"
       @onchange="@(e => ValueChanged.InvokeAsync(e.Value?.ToString() ?? string.Empty))" />

@code {
    [Parameter] public string Value { get; set; } = string.Empty;
    [Parameter] public EventCallback<string> ValueChanged { get; set; }
}
```

```razor
@* DescriptionInput.razor — explicit callback, no @bind-Value sugar *@
<textarea @onchange="@(async e => await OnValueChanged.InvokeAsync(e.Value?.ToString() ?? string.Empty))">@Value</textarea>

@code {
    [Parameter] public string Value { get; set; } = string.Empty;
    [Parameter] public EventCallback<string> OnValueChanged { get; set; }
}
```

```razor
@* OrderNew.razor — parent wiring side by side *@
<NameInput @bind-Value="_name" />
<DescriptionInput Value="@_description" OnValueChanged="v => _description = v" />
```

**Rule:** Child components in this pattern must remain stateless and presentation-only. They receive the current value as a parameter and relay change notifications upward; they must not hold their own copy of the value being edited. The parent is the single source of truth for the field. A child that caches the value locally will drift out of sync when the parent re-renders with a new value.

**Connection to the broader integration architecture:** This dispatch-event-upward / receive-value-downward shape is the same communication model intended between Blazor components and their Angular host shell in this project's wider integration effort. Blazor components embedded in an Angular shell should dispatch events outward and accept data inward as parameters — they should not reach across the boundary to manage Angular-owned state. This in-app pattern is a smaller-scale rehearsal of that same boundary contract.

---

## Pattern: CascadingValue and CascadingValueSource

**Canonical example:** [Components/Pages/CascadingDemo.razor](../Components/Pages/CascadingDemo.razor) + [Components/MiddleLayer.razor](../Components/MiddleLayer.razor) + [Components/PermissionAwareLeaf.razor](../Components/PermissionAwareLeaf.razor)

This pattern addresses a different axis than either of the preceding two:

- **Versus child-to-parent communication (EventCallback):** That pattern requires every intermediate layer to actively participate — declare a parameter, accept the value or callback, and relay it to the next level. Cascading values invert this: the ancestor provides the value once, and any descendant at any depth can consume it directly via `[CascadingParameter]`, with no action required from any component in between.

- **Versus DI state-container services:** A DI service is circuit-scoped and reachable from any component regardless of where it sits in the component tree — there is no structural relationship requirement. A cascading value only reaches components within the subtree of the ancestor that provides it. Two unrelated branches of the component tree cannot share a cascading value the way they can share a DI service.

**The core proof point** is `MiddleLayer.razor`: it sits between `CascadingDemo` (the source) and `PermissionAwareLeaf` (the consumer), yet it contains zero code related to `UserContext` or `IsAdmin` — no `[CascadingParameter]`, no `[Parameter]`, no `@code` logic referencing either. The framework delivers the cascading value directly from the source to `PermissionAwareLeaf` two levels down, bypassing `MiddleLayer` entirely.

**Rule: `CascadingValue` vs. `CascadingValueSource`**

- Use plain `<CascadingValue Value="@_someValue">` when the value is set once and descendants only need it at initial render (`IsFixed="true"`, or omit entirely — fixed is the default when `IsFixed` is not specified and the value doesn't change).
- Use `CascadingValueSource<T>` (registered via `AddCascadingValue` in `Program.cs`, with `isFixed: false`) when descendants need to react live to the value changing after initial render. Mutate the shared object, then call `NotifyChangedAsync()` (parameterless overload) to push the change to all subscribers. Descendants re-render automatically without any manual subscription or `IDisposable` cleanup — unlike the `OnChange` event pattern used with DI state containers.

**Rule: cross-cutting concerns, not feature-specific data.** Cascading values are best suited to values that are relevant broadly across an entire subtree — current user/permissions, theme, locale, UI configuration. They are not well suited to data specific to one feature or flow (e.g., the selected case, an in-progress order draft), where a scoped DI service is more appropriate because it is explicitly named, typed, and testable in isolation.

```razor
@* CascadingDemo.razor — source; no <CascadingValue> wrapper needed in markup *@
@inject UserContext UserContext
@inject CascadingValueSource<UserContext> UserContextSource

<button @onclick="ToggleAdmin">Toggle Admin</button>
<MiddleLayer><PermissionAwareLeaf /></MiddleLayer>

@code {
    private async Task ToggleAdmin()
    {
        UserContext.IsAdmin = !UserContext.IsAdmin;
        await UserContextSource.NotifyChangedAsync();
    }
}
```

```razor
@* MiddleLayer.razor — zero awareness of the cascading value *@
@code {
    [Parameter] public RenderFragment? ChildContent { get; set; }
}
```

```razor
@* PermissionAwareLeaf.razor — consumes the cascading value directly *@
@code {
    [CascadingParameter] public UserContext? CurrentUser { get; set; }
}
```

`CascadingValueSource<T>` must be registered in `Program.cs` via `AddCascadingValue`. The source itself is also registered as a singleton so the providing component can inject it to call `NotifyChangedAsync()`. No `<CascadingValue>` wrapper is needed in markup — the framework discovers the supplier via DI and delivers it to all `[CascadingParameter]` matches within the component tree.

```csharp
// Program.cs
var userContext = new UserContext();
var userContextSource = new CascadingValueSource<UserContext>(userContext, isFixed: false);
builder.Services.AddSingleton(userContext);
builder.Services.AddSingleton(userContextSource);
builder.Services.AddCascadingValue(_ => userContextSource);
```

---

## Critical rule: DI lifetime for Blazor Server

State-container services that hold per-user data **must be registered as Scoped**, never Singleton, in Blazor Server apps.

```csharp
// Correct
builder.Services.AddScoped<CaseSelectionState>();

// Wrong — leaks state across all users
builder.Services.AddSingleton<CaseSelectionState>();
```

A Singleton is instantiated once per server process and shared across every circuit (every user, every tab). One user's selected case becomes visible to all other users. This is confirmed behavior, not a theoretical risk.

**Note on Blazor WebAssembly:** The opposite is true there. In WASM, each user runs their own browser-side .NET process, so Singleton is safe and idiomatic. The Scoped/Singleton distinction is Blazor Server-specific.

Source: [Microsoft Blazor state management documentation](https://learn.microsoft.com/en-us/aspnet/core/blazor/state-management/)

---

## What this PoC deliberately did not cover

- **Cross-circuit / cross-server state** — sharing state between multiple users or across a server farm requires a distributed cache (Redis, SQL, etc.) outside DI.
- **Fluxor or other Flux/Redux-style state management libraries** — structured action/reducer/effect patterns for larger apps with complex shared state. (Fluxor was added as a standalone comparison demo; see the section below.)
- **`ProtectedBrowserStorage`** — persisting state across full browser restarts using `localStorage`/`sessionStorage` with server-side encryption.
- **Deeper explicit bubbling through multiple intermediate layers** — child-to-parent relaying through more than one level of explicitly-participating intermediates (each declaring a parameter and relaying an EventCallback) was considered but not built in this PoC. `CascadingValue` already addresses the "skip multiple layers" need more idiomatically, making multi-level explicit bubbling an uncommon pattern in practice.
- **Authentication-scoped state** — state tied to an authenticated user identity across multiple circuits or sessions.
- **Optimistic UI / conflict resolution** — handling concurrent updates to shared state from multiple components.
- **Form validation** — `EditForm`, `DataAnnotationsValidator`, and `ValidationSummary` for validating user input before state is written. The Order wizard intentionally skips validation to keep the state-management demonstration uncluttered.
- **Persisting confirmed data** — the Order wizard's "Confirm" button clears draft state and navigates away, but does not save the order anywhere. Wiring the confirmed data to a repository, database, or API call is outside the scope of this PoC.
- **Validation of child component inputs** — `NameInput` and `DescriptionInput` relay raw strings to the parent with no validation. Integrating them with `EditForm` and `DataAnnotationsValidator`, or propagating validation errors back down from parent to child, is outside the scope of this PoC.

---

## Third-party comparison: Fluxor (Redux-style state management)

Fluxor is a .NET implementation of the Redux pattern for Blazor. The core concepts are:

- **State** — an immutable record decorated with `[FeatureState]`; Fluxor registers it with the store automatically via assembly scanning.
- **Actions** — plain records (or classes) that describe intent, carrying only the data relevant to the operation. No logic.
- **Reducers** — static methods decorated with `[ReducerMethod]` that accept the current state and an action and return a **new** state record. Never mutate in place.
- **`IDispatcher`** — injected into any component that needs to initiate a state change; calling `Dispatcher.Dispatch(new SomeAction(...))` sends the action through the reducer pipeline.
- **`IState<T>`** — injected into any component that needs to read state reactively; `State.Value` returns the current immutable snapshot, and components inheriting `FluxorComponent` re-render automatically when the state changes — no manual `StateHasChanged` call required.

Two working examples in this repo:

- **Single-page demo** (`/fluxor-demo`): [FluxorDemo/TagState.cs](../FluxorDemo/TagState.cs), [FluxorDemo/SelectTagAction.cs](../FluxorDemo/SelectTagAction.cs), [FluxorDemo/TagReducer.cs](../FluxorDemo/TagReducer.cs), [Components/Pages/FluxorTagDemo.razor](../Components/Pages/FluxorTagDemo.razor) — shows dispatch and reactive re-render on one page.
- **Cross-page demo** (`/fluxor-compose` → `/fluxor-preview`): [FluxorDemo/TaskDraftState.cs](../FluxorDemo/TaskDraftState.cs), [FluxorDemo/TaskDraftReducer.cs](../FluxorDemo/TaskDraftReducer.cs), [Components/Pages/FluxorComposeTask.razor](../Components/Pages/FluxorComposeTask.razor), [Components/Pages/FluxorTaskPreview.razor](../Components/Pages/FluxorTaskPreview.razor) — shows that the Fluxor store is circuit-scoped and persists across navigation, just like a scoped DI service. Page A sets title, priority, and tags; Page B reads them; navigating back shows the state is still there. The `TagState` slice is shared between both demos, demonstrating two pages sharing one state slice.

### Comparison with the built-in patterns

| Aspect | Built-in pattern (this repo) | Fluxor |
|---|---|---|
| **State mutation** | Direct field mutation in the state-container service; setter raises `OnChange` | Immutable records; pure reducer functions return a new state record — in-place mutation is structurally prevented |
| **Triggering re-render** | Subscribing components call `StateHasChanged` manually inside the `OnChange` handler | Automatic via `IState<T>` — components inheriting `FluxorComponent` re-render with no manual call; `IDisposable` subscription management not required |
| **Boilerplate per state slice** | One class with a backing field, a property, and an `OnChange` event | One `[FeatureState]` record + one or more action records + one reducer class |
| **Debugging / tooling** | None built-in; breakpoints and logging only | Redux DevTools-style time-travel debugging available via browser extension integration |

### Recommendation

For this project's current needs — a small number of independent state slices, no complex action composition, no cross-cutting requirement for strict immutability enforcement — the built-in patterns documented earlier in this file are sufficient and preferred. They involve less boilerplate, require no third-party dependency, and do not require team members to learn Redux-specific conventions (actions, reducers, effects, middleware) to make a simple state change.

Fluxor would become worth reconsidering if the app's state logic grows to involve:

- **Many interrelated slices** — where changes to one slice must consistently cascade to others in a predictable, auditable way.
- **Complex or chained action sequences** — where a single user action triggers a series of discrete state transitions that need to be traceable individually.
- **Async side-effects coordinated across multiple slices** — Fluxor Effects provide a structured, testable pattern for async work (API calls, timers) that reacts to dispatched actions and dispatches follow-up actions on completion.
- **A real need for time-travel debugging** — inspecting how state changed step-by-step during development or QA.

**Note for completeness:** Fluxor was added to this repo purely for comparison and demonstration; it is not the recommended default for this project. The built-in patterns remain the actual recommended approach per the decision table earlier in this document.
