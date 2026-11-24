using System.Linq;
using FactorySim.App.Parts;
using FactorySim.Core.Scenes;
using Godot;

namespace FactorySim.App;

/// <summary>Plan 19, part of the "ui" self-test: a floor text and a sign on a post with a text, saved, opened and duplicated.</summary>
public partial class SelfTest
{
    private const string SignText1 = "Afkeur é\nStation 1";
    private const string SignText2 = "Invoer ë";

    /// <summary>Builds the two signs and checks the text through save, open, duplicate and undo; leaves them in view for the screenshot.</summary>
    private void RunUiSigns()
    {
        Editor.SetDesignMode(true);
        Editor.NewScene();

        // 1. Place both, set a text with an accent and a line break.
        SceneItem? post = Editor.Place("sign_post", 0.0f, -0.5f);
        bool fields = Editor.ParamFieldNames.SequenceEqual(new[] { SignText.SizeParam });
        Editor.SetSelectedText(SignText1);
        bool set = Editor.SelectedText == SignText1;
        bool undo = Editor.Undo() && Editor.SelectedText is null && Editor.Redo() && Editor.SelectedText == SignText1;
        SceneItem? floor = Editor.Place("floor_text", 0.0f, 0.75f);
        Editor.SetSelectedText(SignText2);
        Editor.SetSelectedParam(SignText.SizeParam, (double)SignSize.Large);
        SortersCheck("sign text set, size field and undo", post is not null && floor is not null && fields && set && undo);

        // 2. Save and open: the texts are still there.
        string saved = "user://selftest/signs_test" + SceneEditor.FileExtension;
        bool save = Editor.SaveTo(saved) is null;
        Editor.NewScene();
        bool load = Editor.LoadFrom(saved) is null;
        bool kept = Main.Scene.Find(post!.Id)?.Text == SignText1 && Main.Scene.Find(floor!.Id)?.Text == SignText2
            && Main.Scene.Find(floor.Id)?.Params?[SignText.SizeParam] == (double)SignSize.Large;
        SortersCheck("sign texts saved and opened", save && load && kept);

        // 3. Duplicate: the copy has the same text.
        Editor.Select(post.Id);
        SceneItem? copy = Editor.DuplicateSelected();
        bool duplicated = copy is not null && copy.Text == SignText1;
        if (copy is not null)
        {
            Editor.DeleteSelected();
        }

        SortersCheck("duplicated sign keeps its text", duplicated && Main.Scene.Items.Count == 2);

        // The parts show the text: two sides on the post, one label flat on the floor.
        Sign? postPart = Main.View.NodeOf(post.Id) as Sign;
        Sign? floorPart = Main.View.NodeOf(floor.Id) as Sign;
        bool labels = postPart is { Labels.Count: 2 } && postPart.Labels.All(label => label.Text == SignText1)
            && floorPart is { Labels.Count: 1 } && floorPart.Labels[0].Text == SignText2
            && floorPart.Labels[0].FontSize > postPart.Labels[0].FontSize;
        bool noColliderOnFloor = floorPart is not null && !floorPart.GetChildren().OfType<StaticBody3D>().Any()
            && postPart is not null && postPart.GetChildren().OfType<StaticBody3D>().Any();
        GD.Print($"SELFTEST ui signs: labels={labels} colliders={noColliderOnFloor} overlap='{Editor.OverlapWarning}'");
        SortersCheck("sign parts show the text", labels && noColliderOnFloor && Editor.OverlapWarning.Length == 0);

        // 4. In view for the screenshot: from the front, a little above.
        Editor.Select(null);
        Main.Camera.SetView(0.0f, -42.0f);
        Main.FocusOn(floor.Id);
        Main.Camera.Pivot = new Vector3(0.0f, 0.5f, 0.1f);
        Main.Camera.Distance = 3.2f;
    }
}
