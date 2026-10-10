using UnityEngine;

namespace FiresMounts
{
    /// <summary>
    /// 0.1.124: the water mounts' interact sphere. It follows the saddle point across the water but never sinks below the live
    /// surface (Floating.GetWaterLevel: Galaxies' tide included) + Above, and keeps a world radius of Radius metres at any scale, so
    /// E reaches the mount from a boat or the shore.
    /// </summary>
    public class AboveWaterInteract : MonoBehaviour
    {
        private const float Above = 0.6f, Radius = 1.2f;
        private SphereCollider _sphere;
        private WaterVolume _volume;

        private void Awake() => _sphere = GetComponent<SphereCollider>();

        private void LateUpdate()
        {
            Transform seat = transform.parent;
            if (seat == null || _sphere == null) return;
            Vector3 at = seat.position;
            float surface = Floating.GetWaterLevel(at, ref _volume);
            if (at.y < surface + Above) at.y = surface + Above;
            transform.position = at;
            float lossy = Mathf.Max(0.0001f, transform.lossyScale.x);
            _sphere.radius = Radius / lossy;
        }
    }
}
