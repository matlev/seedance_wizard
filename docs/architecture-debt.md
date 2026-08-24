# Architecture debt register

Status: active

Record only approved, temporary architectural exceptions. Each entry must identify the rule, exact scope, reason, owner/removal condition, verification, and a prohibition on copying the exception.

## ARCH-DEBT-001 — App direct Infrastructure references outside Bootstrap

- **Rule and exception:** App composition belongs in `Bootstrap/ApplicationRuntime`, but the listed existing App files directly import `ReelForge.Infrastructure` for current implementation types or static policies.
- **Allowed current scope:**
  - `src/ReelForge.App/MainWindow.xaml.cs`
  - `src/ReelForge.App/Views/Settings/SettingsWindow.xaml.cs`
  - `src/ReelForge.App/Views/Dialogs/AssetNameDialog.xaml.cs`
  - `src/ReelForge.App/Views/Dialogs/OpenProjectDialog.xaml.cs`
  - `src/ReelForge.App/Views/Editing/CompositionAuditionController.cs`
  - `src/ReelForge.App/Views/Editing/CompositionRenderCoordinator.cs`
  - `src/ReelForge.App/Views/Editing/CompositionWorkspaceCoordinator.cs`
  - `src/ReelForge.App/Views/Generation/GenerationContinuationCoordinator.cs`
  - `src/ReelForge.App/Views/Generation/GenerationWorkspaceCoordinator.cs`
  - `src/ReelForge.App/Views/MediaPreparation/FramePreparationCoordinator.cs`
  - `src/ReelForge.App/Views/MediaPreview/MediaPreviewCoordinator.cs`
  - `src/ReelForge.App/Views/ProjectMedia/MediaImportCoordinator.cs`
  - `src/ReelForge.App/Views/ProjectMedia/ProjectMediaOperationsCoordinator.cs`
- **Reason:** Milestone 3 preserved behavior while establishing the composition root and feature-owned WPF structure; these remaining imports are bounded legacy seams.
- **Guardrails:** Do not add direct `ReelForge.Infrastructure` imports to any other App file. Do not construct new infrastructure services outside `Bootstrap/ApplicationRuntime`; inject the required Application contract or existing runtime-owned dependency instead. Do not expand this exception while implementing unrelated features.
- **Removal condition:** Remove each listed import when its consuming presentation code can depend on an Application contract or a Bootstrap-provided facade without moving presentation behavior into Infrastructure. Close this record only when no App file outside `Bootstrap` imports Infrastructure.
- **Verification:** `ReelForge.App.Tests.ArchitectureBoundaryTests.InfrastructureReferencesOutsideBootstrapMatchArchDebt001Allowlist` checks imports and fully qualified references against an exact executable allowlist, so additions and removals require the register and test to change together.

## ARCH-DEBT-002 — App infrastructure construction outside Bootstrap

- **Rule and exception:** Concrete infrastructure services are constructed in `Bootstrap/ApplicationRuntime`; the following existing presentation code constructs a concrete infrastructure type outside that composition root:
  - `src/ReelForge.App/MainWindow.xaml.cs` → `PhysicalAssetSelectionPreparationService`
  - `src/ReelForge.App/Views/Editing/CompositionWorkspaceCoordinator.cs` → `Sha256ContentHashService`
  - `src/ReelForge.App/Views/Generation/GenerationWorkspaceCoordinator.cs` → `FakeVideoGenerationProvider`
- **Guardrails:** Do not add any new file/type construction pair outside `Bootstrap/ApplicationRuntime`. Do not copy these exceptions into another feature.
- **Removal condition:** Replace each construction with a runtime- or Application-owned interface/service injected from the composition root, without moving presentation behavior into Infrastructure. Close this record only when none of the listed constructions remains.
- **Verification:** `ReelForge.App.Tests.ArchitectureBoundaryTests.ConcreteInfrastructureAndPlatformConstructionOutsideBootstrapMatchesArchDebt002Allowlist` keeps the executable allowlist exact. `PresentationDoesNotUseCompositionRootBypasses` blocks concrete-type aliases, common dynamic construction, and service location. These lexical checks complement, rather than replace, architectural review.
