using FischMacroCS.Core;
using OpenCvSharp;

namespace FischMacroCS.Tests;

public class BundledWorkflowTemplateTests
{
    [Fact]
    public void CrateTemplatesWorkWhenZipLaunchProvidesOnlyTheExecutable()
    {
        string missing = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        using var bundled = new CrateVision(missing, useBundledTemplates: true);
        Assert.Empty(bundled.MissingTemplates());
        using var explicitDirectory = new CrateVision(missing);
        Assert.Equal(CrateVision.Names, explicitDirectory.MissingTemplates());
        using var frame = Cv2.ImRead(System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", "crate_inventory_dense.png"));
        Assert.NotNull(bundled.Find(frame, "bag"));
        Assert.NotNull(bundled.Find(frame, "search"));
        Assert.NotNull(bundled.Find(frame, "item"));
    }
    [Fact]
    public void AquariumTemplatesWorkWhenExecutableHasNoExternalAssets()
    {
        string missingDirectory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        using var vision = new TemplateWorkflowVision(missingDirectory, useBundledTemplates: true);
        Assert.Empty(vision.MissingTemplates(AquariumWorkflow.TemplateNames));
        Assert.Equal(["unknown-template"], vision.MissingTemplates(["unknown-template"]));

        using var stream = typeof(TemplateWorkflowVision).Assembly.GetManifestResourceStream(
            "FischMacroCS.Assets.Workflows.aquarium-navigation.png")!;
        using var bytes = new System.IO.MemoryStream();
        stream.CopyTo(bytes);
        using var template = Cv2.ImDecode(bytes.ToArray(), ImreadModes.Color);
        using var frame = new Mat(1353, 2400, MatType.CV_8UC3, Scalar.All(20));
        int x = frame.Width / 2 + (int)(AquariumWorkflow.Navigation.XFromCenterInHeights * frame.Height);
        int y = (int)(AquariumWorkflow.Navigation.YInHeights * frame.Height);
        using (var destination = new Mat(frame, new Rect(x - template.Width / 2, y - template.Height / 2,
            template.Width, template.Height))) template.CopyTo(destination);
        Assert.True(vision.Find(frame, AquariumWorkflow.Navigation).Found);
    }

    [Fact]
    public void ExplicitTemplateDirectoryStillReportsMissingAssets()
    {
        using var vision = new TemplateWorkflowVision(System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        Assert.Equal(AquariumWorkflow.TemplateNames, vision.MissingTemplates(AquariumWorkflow.TemplateNames));
    }
}
