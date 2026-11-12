using UnityEngine;

namespace FirstPersonSokoban.Interaction
{
    /// <summary>瞄准箱子时，在「箱子将被推入的格子」显示一块地面标记。</summary>
    public sealed class PushPreviewView : MonoBehaviour
    {
        public static PushPreviewView Create(Transform parent)
        {
            var root = new GameObject("Push Target Preview");
            root.transform.SetParent(parent, false);
            PushPreviewView view = root.AddComponent<PushPreviewView>();
            view.Build();
            root.SetActive(false);
            return view;
        }

        private void Build()
        {
            GameObject plate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            plate.name = "Target Cell Plate";
            Collider collider = plate.GetComponent<Collider>();
            if (collider != null) { collider.enabled = false; Destroy(collider); }
            plate.transform.SetParent(transform, false);
            plate.transform.localScale = new Vector3(1.72f, 0.03f, 1.72f);

            Shader shader = Shader.Find("Standard");
            Material material = shader != null ? new Material(shader) : null;
            if (material != null)
            {
                material.color = new Color(0.35f, 0.88f, 0.76f, 1f);
                plate.GetComponent<Renderer>().sharedMaterial = material;
            }
        }

        public void Show(Vector3 cellWorld)
        {
            transform.position = cellWorld + Vector3.up * 0.04f;
            gameObject.SetActive(true);
        }

        public void Hide() => gameObject.SetActive(false);
    }
}
