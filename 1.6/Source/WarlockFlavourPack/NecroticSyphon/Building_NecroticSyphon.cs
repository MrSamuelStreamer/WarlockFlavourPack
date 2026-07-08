using RimWorld;
using UnityEngine;
using Verse;

namespace WarlockFlavourPack.NecroticSyphon;

/// <summary>
/// Custom Building subclass for the Necrotic Syphon.
/// Swaps to the active texture (<c>MSS_NecroticSyphonActive</c>) while charged.
/// </summary>
public class Building_NecroticSyphon : Building
{
    private Graphic _activeGraphic;

    private Graphic ActiveGraphic
    {
        get
        {
            if (_activeGraphic == null)
            {
                _activeGraphic = GraphicDatabase.Get<Graphic_Single>(
                    "Things/Building/MSS_NecroticSyphonActive",
                    def.graphicData.shaderType.Shader,
                    def.graphicData.drawSize,
                    Color.white);
                // Inherit graphicData so DrawOffsetFull (and other data-driven props) match the base graphic.
                _activeGraphic.data = def.graphicData;
            }
            return _activeGraphic;
        }
    }

    public override Graphic Graphic
    {
        get
        {
            Comp_NecroticSyphon comp = GetComp<Comp_NecroticSyphon>();
            if (comp is { IsCharged: true })
                return ActiveGraphic;
            return base.Graphic;
        }
    }

    public override void DrawExtraSelectionOverlays()
    {
        base.DrawExtraSelectionOverlays();
        var comp = GetComp<Comp_NecroticSyphon>();
        if (comp != null)
            GenDraw.DrawRadiusRing(Position, comp.Props.radius);
    }
}
