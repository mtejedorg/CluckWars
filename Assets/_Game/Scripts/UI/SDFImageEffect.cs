using UnityEngine;
using UnityEngine.UI;

namespace CluckWars.UI
{
    [ExecuteAlways]
    [RequireComponent(typeof(Graphic))]
    [AddComponentMenu("CluckWars/UI/SDF Image Effect")]
    public class SDFImageEffect : BaseMeshEffect
    {
        private RectTransform _rectTransform;

        protected override void Awake()
        {
            base.Awake();
            _rectTransform = GetComponent<RectTransform>();
        }

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive()) return;
            if (_rectTransform == null) _rectTransform = GetComponent<RectTransform>();

            Vector2 size = _rectTransform.rect.size;
            UIVertex vert = new UIVertex();
            for (int i = 0; i < vh.currentVertCount; i++)
            {
                vh.PopulateUIVertex(ref vert, i);
                // Pass the absolute dimensions via UV1 so the shader can compute the SDF
                vert.uv1 = size;
                vh.SetUIVertex(vert, i);
            }
        }

        protected override void OnRectTransformDimensionsChange()
        {
            base.OnRectTransformDimensionsChange();
            if (graphic != null)
                graphic.SetVerticesDirty();
        }
    }
}
