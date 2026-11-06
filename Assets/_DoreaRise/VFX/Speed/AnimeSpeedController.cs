using UnityEngine;

[RequireComponent(typeof(Renderer))]
public class AnimeSpeedController : MonoBehaviour
{
    [Header("Settings")]
    [Tooltip("ตัวคูณความเร็วเพื่อให้ยืดมากขึ้น")]
    public float speedMultiplier = 0.5f;
    [Tooltip("จำกัดความยาวสูงสุดของการยืด")]
    public float maxStretch = 3.0f;
    [Tooltip("ความเร็วขั้นต่ำที่จะเริ่มให้เอฟเฟกต์ทำงาน")]
    public float minimumSpeedToBlur = 2.0f;

    private System.Collections.Generic.List<Material> instancedMaterials = new System.Collections.Generic.List<Material>();
    private Vector3 lastPosition;
    private int velocityPropertyID;

    void Start()
    {
        Renderer rend = GetComponent<Renderer>();
        velocityPropertyID = Shader.PropertyToID("_Velocity");

        // วนลูปหา Material ใน Array และเก็บไว้ทุกตัวที่ใช้ Shader นี้
        Material[] mats = rend.materials;
        foreach (Material m in mats)
        {
            if (m.HasProperty("_Velocity"))
            {
                instancedMaterials.Add(m);
            }
        }

        if (instancedMaterials.Count == 0)
        {
            Debug.LogWarning("ไม่พบ Material AnimeSpeedBlur! กรุณาตรวจสอบให้แน่ใจว่าได้ใส่ไว้แล้ว");
        }

        lastPosition = transform.position;
    }

    void Update()
    {
        if (instancedMaterials.Count == 0) return;

        // คำนวณความเร็ว (Velocity) ในโลกจริงจากระยะทางที่เปลี่ยนไป
        Vector3 currentVelocity = (transform.position - lastPosition) / Time.deltaTime;
        lastPosition = transform.position;

        float speed = currentVelocity.magnitude;
        Vector3 finalVelocityToShader = Vector3.zero;

        // ถ้าวิ่งเร็วเกินค่าที่กำหนด ถึงจะเริ่มทำเอฟเฟกต์
        if (speed > minimumSpeedToBlur)
        {
            // ปรับทิศทางความเร็วด้วยตัวคูณ และจำกัดความยาวไม่ให้ทะลุจอ
            finalVelocityToShader = currentVelocity * speedMultiplier;
            if (finalVelocityToShader.magnitude > maxStretch)
            {
                finalVelocityToShader = finalVelocityToShader.normalized * maxStretch;
            }
        }
        else
        {
            // ถ้าหยุดวิ่ง ค่อยๆ ลดเอฟเฟกต์ลงให้ดูนุ่มนวล (Lerp)
            Vector3 currentShaderVel = instancedMaterials[0].GetVector(velocityPropertyID);
            finalVelocityToShader = Vector3.Lerp(currentShaderVel, Vector3.zero, Time.deltaTime * 10f);
        }

        // ส่งค่ากลับเข้าไปที่ Shader สำหรับทุก Material ที่พบ
        foreach (Material m in instancedMaterials)
        {
            m.SetVector(velocityPropertyID, finalVelocityToShader);
        }
    }
}