using Avalonia;
using Avalonia.Fonts.Inter;
using Avalonia.Headless;
using Avalonia.Threading;

namespace DiezPublishingStudio;

/// <summary>
/// CI-only Avalonia harness. It constructs the real MainWindow and now mounts the exact same
/// production module list as the installed app, preventing test/user navigation from diverging.
/// </summary>
internal static class HeadlessCiHarness
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions
            {
                UseHeadlessDrawing = false
            })
            .WithInterFont()
            .LogToTrace();

    public static async Task<int> RunAsync(string[] args)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(HeadlessCiHarness));
        return await session.Dispatch(async () =>
        {
            MainWindow? window = null;
            try
            {
                window = new MainWindow(null);
                var failures = App.AttachProductionModules(window);
                if (failures.Count > 0)
                    throw new InvalidOperationException("Moduli produzione non montati nel gate headless: " + string.Join(" | ", failures));

                window.Show();
                Dispatcher.UIThread.RunJobs();

                if (args.Any(a => string.Equals(a, "--ui-raster-probe", StringComparison.OrdinalIgnoreCase)))
                {
                    await SingleWindowPhysicalScreenshotProbe.RunAsync(window);
                    return 0;
                }

                if (args.Any(a => string.Equals(a, "--ui-flow-contract", StringComparison.OrdinalIgnoreCase)))
                {
                    await RunFlowContractAsync(window);
                    return 0;
                }

                Dispatcher.UIThread.RunJobs();
                return 0;
            }
            catch (Exception ex)
            {
                var raster = args.Any(a => string.Equals(a, "--ui-raster-probe", StringComparison.OrdinalIgnoreCase));
                var flow = args.Any(a => string.Equals(a, "--ui-flow-contract", StringComparison.OrdinalIgnoreCase));
                var errorFile = raster
                    ? "ui-raster-error.txt"
                    : flow
                        ? "ui-flow-contract.txt"
                        : "ui-headless-startup-error.txt";
                try
                {
                    var path = Path.Combine(AppContext.BaseDirectory, errorFile);
                    if (flow)
                        File.AppendAllText(path, Environment.NewLine + "ERROR | " + DateTimeOffset.Now.ToString("O") + Environment.NewLine + ex + Environment.NewLine);
                    else
                        File.WriteAllText(path, ex.ToString());
                }
                catch { }
                return raster ? 3 : 2;
            }
            finally
            {
                try { Dispatcher.UIThread.RunJobs(); } catch { }
                try { window?.Close(); } catch { }
                try { Dispatcher.UIThread.RunJobs(); } catch { }
            }
        }, CancellationToken.None);
    }

    private static async Task RunFlowContractAsync(MainWindow window)
    {
        var resultFile = Path.Combine(AppContext.BaseDirectory, "ui-flow-contract.txt");
        if (File.Exists(resultFile)) File.Delete(resultFile);
        FlowProgress(resultFile, "START", "flow-contract");

        if (!ExitConfirmationUi.IsAttached(window))
            throw new InvalidOperationException("La conferma uscita non è collegata al MainWindow.");

        // Keep a durable breadcrumb before and after every sub-contract. The workflow has an external
        // process timeout, so even a hard UI hang leaves the last START marker available for diagnosis.
        await RunPhaseAsync(resultFile, nameof(SingleWindowNativeClickContract), () => SingleWindowNativeClickContract.RunAsync(window));
        await RunPhaseAsync(resultFile, nameof(FlowContractRootMountProbe), () => FlowContractRootMountProbe.EnsureMountedAsync(window));
        await RunPhaseAsync(resultFile, nameof(SingleWindowV11ContractProbe), () => SingleWindowV11ContractProbe.RunAsync(window));
        await RunPhaseAsync(resultFile, nameof(MultiSubjectUiContractProbe), () => MultiSubjectUiContractProbe.RunAsync(window));
        await RunPhaseAsync(resultFile, nameof(StructuredSceneUiContractProbeV2), () => StructuredSceneUiContractProbeV2.RunAsync(window));
        await RunPhaseAsync(resultFile, nameof(SingleWindowProjectResumeUi), () => SingleWindowProjectResumeUi.RunContractAsync(window));
        await RunPhaseAsync(resultFile, nameof(SingleWindowResponseReviewUiContractProbe), () => SingleWindowResponseReviewUiContractProbe.RunAsync(window));

        FlowProgress(resultFile, "OK", "flow-contract");
        File.AppendAllText(resultFile,
            "OK\nSW-FLOW-12\nstartup=headless-real-ui-tree\nproduction-modules=exact-app-list\nproduction-entry=native-v11\nreal-click-quantity-to-prompt=yes\nbook-type=visible\nbook-title=visible-and-package-naming\nbook-type-back=works\nbook-type-page=native-host\nquantity-change-type=absent\nquantity-field=native-numeric\nquantity-visible-all-steps=yes\nessential-editors=native-host\nvisual-subject-environment=native-visible\nvisual-per-image-overrides=yes\nmulti-subject=optional-1-12\nmulti-subject-description=reuses-theme-box\nmulti-subject-id=stable\nmulti-subject-consistent=per-subject\nstructured-scenes=optional\nstructured-scene-id=stable\nstructured-scene-mode=environment-switch\nstructured-scene-consistent-membership=subject-id\nstructured-scene-membership-ui=listbox\ncoloring-style=native-visible\ncoloring-style-single-choice=yes\ncustom-style=hard-authority\ncustom-style-library=explicit-opt-in\ncoloring-bold-easy=bidirectional-hard\ncoloring-cozy=bidirectional-hard\ncoloring-binary-bw=fixed\nline-thickness=dropdown\nimage-specs=visible\nkdp-trim-presets=yes\neditable-inputs=native-textbox-safe-startup\nactive-project=kept-until-replace-or-exit\nhome-resume=book-type\nconsistent-on=criteria-native-visible\nconsistency-notes=native-visible\nconsistency-levels=3\nconsistency-free-strategies=USER,AI,MIXED\nconsistency-free-user=description-required\nconsistency-free-ai=description-optional\nconsistency-free-mixed=description-required\nbleed=image-generation-removed\nprompt-editors=native-3\nundo=ctrl-z\nredo=ctrl-y\nprompt-semantic-engine=3.0\nprompt-provider-compiler=3.5\nprompt-provider-compiler-current=3.6\nprompt-synthesis=creative-director\nprompt-generated-technical-language=en\nprompt-legacy-technical-injection=disabled\nprompt-profile-isolation=yes\nprompt-work-unit-output=1\nprompt-render-field=image_generation_prompt\nprompt-context-single-source=yes\nprompt-atomic-subject=yes\nprompt-structured-scene=yes\nprompt-scene-participants=hard\nprompt-style-hard=yes\nprompt-bold-easy-hard=yes\nprompt-cozy-hard=yes\nprompt-composition-hard=yes\nrenderer-prompt-scope=visual-only\nmanual-render-plan=1.3\nmanual-render-fresh-per-work-unit=yes\nmanual-render-fresh-context-owner=executor\nmanual-render-call-isolation=no-prior-image-reference\nmanual-render-chat-session=conditional-on-provider-visual-state\nmanual-clean-room-queue=1.0\nmanual-clean-room-launcher=yes\nmanual-clean-room-chat=temporary-or-new-blank-per-work-unit\nmanual-clean-room-one-attempt=yes\nmanual-partial-response=one-per-work-unit\nmanual-response-bundle=1.0\nmanual-response-bundle-shape=one-outer-zip-n-inner-zips\nmanual-response-bundle-manifest=response-bundle-manifest.json\nmanual-response-bundle-import=single-zip-preferred\nmanual-partial-response-import=multi-select-compatible-fallback\npackage-naming=diez-title-versioned\nvisual-context=V3\nresponse-import=audited-v2\nresponse-failed-review=scrollable-audited\nvisual-prompt-pipeline=single-source\nvision-validation=semantic-v1\nvision-transport=prompt-pack-and-api-contract\nvision-real-asset=authoritative\nvision-hard-fail=approval-blocked\nvision-style-match=hard\nvision-bold-easy-match=hard\nvision-cozy-match=hard\nvision-scene-participants=hard\nvision-review=human-decision\nSW-FLOW-11\nSW-FLOW-10\nstartup=guided\nquantity-field=visible\ncoloring-style=visible\ncoloring-profile=rich\nsubject-environment=visible\nimage-resolution-classes=HD,FHD,2K,4K,8K,PRINT,CUSTOM\nimage-resolution-preserves-aspect=yes\nimage-specs-in-prompt=yes\nimage-collection-color-modes=visible\nillustrated-book-shares-illustration-profile=yes\nillustrated-book-not-coloring=yes\nresolution-classes-all-visual-book-types=yes\nconsistent-off=criteria-hidden\nconsistent-on=criteria-visible\nprompt-target-ai=visible\nprompt-target-catalog=central\nprompt-editors=3\n");
    }

    private static async Task RunPhaseAsync(string resultFile, string phase, Func<Task> run)
    {
        FlowProgress(resultFile, "START", phase);
        await run();
        FlowProgress(resultFile, "OK", phase);
    }

    private static void FlowProgress(string resultFile, string state, string phase)
    {
        File.AppendAllText(resultFile,
            state + " | " + DateTimeOffset.Now.ToString("O") + " | " + phase + Environment.NewLine);
    }
}
