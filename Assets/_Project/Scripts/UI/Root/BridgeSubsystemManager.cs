using System;
using OSE.App;
using OSE.Content;
using OSE.Runtime;
using UnityEngine;

namespace OSE.UI.Root
{
    /// <summary>
    /// Owns all plain-C# sub-system instances created by <see cref="PartInteractionBridge"/>.
    /// Extracted to reduce <c>OnEnable</c> complexity and group sub-system initialization in one place.
    /// </summary>
    internal sealed class BridgeSubsystemManager
    {
        private readonly IBridgeContext       _ctx;
        private readonly Func<PreviewSceneSetup> _getSetup;

        public UseStepHandler                   UseHandler            { get; private set; }
        public ConnectStepHandler               ConnectHandler        { get; private set; }
        public PlaceStepHandler                 PlaceHandler          { get; private set; }
        public SubassemblyPlacementController   SubassemblyController { get; private set; }
        public StepExecutionRouter              Router                { get; private set; }
        public PartVisualFeedbackManager        VisualFeedback        { get; private set; }
        public DragController                   Drag                  { get; private set; }
        public HintManager                      HintManager           { get; private set; }
        public PreviewSpawnManager              PreviewManager        { get; private set; }
        public StepFocusComputer                FocusComputer         { get; private set; }
        public ToolActionExecutor               ToolAction            { get; private set; }
        public SelectionCoordinator             Selection             { get; private set; }
        public StepStateResponder               StepResponder         { get; private set; }
        public DockArcCoordinator               DockArc               { get; private set; }
        public IConfirmInspectionService        ConfirmInspection     { get; private set; }

        public BridgeSubsystemManager(IBridgeContext ctx, Func<PreviewSceneSetup> getSetup)
        {
            _ctx      = ctx;
            _getSetup = getSetup;
        }

        /// <summary>
        /// Idempotent: creates sub-systems that do not yet exist and registers them.
        /// Safe to call on every <c>OnEnable</c> — already-created instances are reused.
        /// </summary>
        public void EnsureInitialized()
        {
            UseHandler ??= new UseStepHandler(_ctx);
            Router     ??= new StepExecutionRouter();
            Router.Register(StepFamily.Use,     UseHandler);
            Router.Register(StepFamily.Confirm, new ConfirmStepHandler());
            ConnectHandler ??= new ConnectStepHandler(_ctx);
            Router.Register(StepFamily.Connect, ConnectHandler);
            SubassemblyController ??= new SubassemblyPlacementController(_ctx);
            ServiceRegistry.Register<ISubassemblyPlacementService>(SubassemblyController);
            VisualFeedback  ??= new PartVisualFeedbackManager(_ctx);
            Drag            ??= new DragController(_getSetup);
            HintManager     ??= new HintManager(_ctx);
            PreviewManager  ??= new PreviewSpawnManager(_ctx);
            FocusComputer   ??= new StepFocusComputer(_ctx);
            PlaceHandler    ??= new PlaceStepHandler(_ctx);
            Router.Register(StepFamily.Place, PlaceHandler);
            ToolAction    ??= new ToolActionExecutor(_ctx);
            Selection     ??= new SelectionCoordinator(_ctx);
            StepResponder ??= new StepStateResponder(_ctx, Selection);
            DockArc       ??= new DockArcCoordinator(_ctx);
            if (ConfirmInspection == null)
            {
                ConfirmInspection = new ConfirmInspectionService();
                ServiceRegistry.Register<IConfirmInspectionService>(ConfirmInspection);
            }
        }

        /// <summary>
        /// Clears visual state and releases services. Call from <c>OnDisable</c>.
        /// The manager itself is retained so sub-systems survive enable/disable cycles.
        /// </summary>
        public void Teardown()
        {
            VisualFeedback?.Clear();
            DockArc?.Clear();
            ToolAction?.ClearToolPreviewIndicator();
            ToolAction?.ClearToolActionTargets();
            Router?.CleanupAll();
            SubassemblyController?.Dispose();
            SubassemblyController = null;
            ServiceRegistry.Unregister<ISubassemblyPlacementService>();
            StepResponder?.SetStartupSyncPending(false);
            ConfirmInspection?.ClearMarkers();
            ConfirmInspection = null;
            ServiceRegistry.Unregister<IConfirmInspectionService>();
        }
    }
}
