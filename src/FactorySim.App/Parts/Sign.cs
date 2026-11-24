using System.Collections.Generic;
using System.Linq;
using FactorySim.Core.Scenes;
using Godot;

namespace FactorySim.App.Parts;

/// <summary>
/// A text in the scene (plan 19): flat on the floor, or on both sides of the plate <c>Panel</c> of a sign
/// on a post. No tags. The text is a <see cref="Label3D"/> with Godot's own font; the model is for selecting
/// and for the looks. The sign on a post gets the thin colliders of the catalogue, the floor text none.
/// </summary>
public partial class Sign : Node3D
{
    /// <summary>Pixel size of the labels: one font pixel is a millimetre.</summary>
    private const float PixelSize = 0.001f;

    /// <summary>Name of the model in the library.</summary>
    public string ModelName { get; set; } = string.Empty;

    /// <summary>True for the floor text, false for the sign on a post.</summary>
    public bool OnFloor { get; set; }

    public string Text { get; set; } = string.Empty;

    public SignSize Size { get; set; } = SignSize.Medium;

    /// <summary>The boxes to build, from <see cref="CatalogAsset.Colliders"/>.</summary>
    public IReadOnlyList<ColliderSpec> Colliders { get; set; } = new List<ColliderSpec>();

    /// <summary>The labels, front first; for the self-test.</summary>
    public IReadOnlyList<Label3D> Labels => _labels;

    private readonly List<Label3D> _labels = new();

    /// <summary>Height of a line of letters in metres, per size.</summary>
    public static float LetterHeight(SignSize size, bool onFloor) => (onFloor, size) switch
    {
        (true, SignSize.Small) => 0.10f,
        (true, SignSize.Large) => 0.26f,
        (true, _) => 0.17f,
        (false, SignSize.Small) => 0.05f,
        (false, SignSize.Large) => 0.11f,
        _ => 0.075f,
    };

    public override void _Ready()
    {
        Node3D model = Models.Instantiate(ModelName);
        AddChild(model);
        PassivePart.AddColliders(this, Colliders);

        if (OnFloor)
        {
            // Lying on the floor: the label's front turns to face up, its top to -Z, so it reads along +X.
            // Just above the top of the plane, whatever its thickness.
            float top = 0.0f;
            foreach (MeshInstance3D mesh in model.FindChildren("*", nameof(MeshInstance3D), recursive: true, owned: false).OfType<MeshInstance3D>())
            {
                top = Mathf.Max(top, ((GlobalTransform.AffineInverse() * mesh.GlobalTransform) * mesh.GetAabb()).End.Y);
            }

            Label3D label = NewLabel(width: 0.0f);
            label.Position = new Vector3(0.0f, top + 0.002f, 0.0f);
            label.RotationDegrees = new Vector3(-90.0f, 0.0f, 0.0f);
            label.Modulate = Colors.White;
            label.OutlineModulate = new Color(0.08f, 0.08f, 0.08f);
            label.OutlineSize = 10;
            AddChild(label);
            _labels.Add(label);
            return;
        }

        // The plate, as a box in this part's frame: the labels go just in front of each face.
        var panel = (MeshInstance3D)Models.Part(model, "Panel");
        Transform3D relative = GlobalTransform.AffineInverse() * panel.GlobalTransform;
        Aabb box = relative * panel.GetAabb();
        Vector3 centre = box.GetCenter();
        float width = box.Size.X * 0.92f;
        const float gap = 0.002f;
        for (int side = 0; side < 2; side++)
        {
            Label3D label = NewLabel(width);
            label.Position = new Vector3(centre.X, centre.Y, side == 0 ? box.End.Z + gap : box.Position.Z - gap);
            label.RotationDegrees = new Vector3(0.0f, side == 0 ? 0.0f : 180.0f, 0.0f);
            label.Modulate = new Color(0.08f, 0.08f, 0.1f);
            label.OutlineSize = 0;
            label.DoubleSided = false;
            AddChild(label);
            _labels.Add(label);
        }
    }

    private Label3D NewLabel(float width)
    {
        var label = new Label3D
        {
            Name = "Text",
            Text = Text,
            PixelSize = PixelSize,
            FontSize = Mathf.RoundToInt(LetterHeight(Size, OnFloor) / PixelSize),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Shaded = false,
            AlphaCut = Label3D.AlphaCutMode.OpaquePrepass,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        if (width > 0.0f)
        {
            label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            label.Width = width / PixelSize;
        }

        label.AddToGroup("sign_text");
        return label;
    }
}
