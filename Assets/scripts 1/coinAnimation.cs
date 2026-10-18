using System.Collections;
using UnityEngine;

public class coinAnimation : MonoBehaviour
{
    [Header("References")]
    [SerializeField] Transform coinPrefab;
    [SerializeField] Transform center;   // where the coins burst out from
    [SerializeField] Transform target;   // where the coins fly to at the end

    [Header("Amount & Shape")]
    [SerializeField] int coinNum = 10;
    [SerializeField] float radius = 3f;  // world units, not pixels
    [SerializeField, Range(0f, 1f)] float minRadiusPercent = 0.6f; // keeps coins from landing too close to the center

    [Header("Timing")]
    [SerializeField] float burstDuration = 0.4f;  // time to fly outward
    [SerializeField] float pauseDuration = 0.15f; // time to hang in the air
    [SerializeField] float flyDuration = 0.6f;    // time to fly to the target
    [SerializeField] float flyStagger = 0f;       // 0 = all return together
    [SerializeField] bool evenCircle = true; // true = perfect ring, false = random scatter
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space))
            SpawnCoins();
    }

    public void OnCoinButtonClick()
    {
        SpawnCoins();
    }

    void SpawnCoins()
    {
        // The camera's right and up directions = a flat surface facing the camera
        Transform cam = Camera.main.transform;
        Vector3 right = cam.right;
        Vector3 up = cam.up;

        for (int i = 0; i < coinNum; i++)
        {
            // 1. Spawn every coin at the center
            Transform coin = Instantiate(coinPrefab, center.position, Quaternion.identity);

            // 2. Choose an angle on the circle
            float angle;
            float dist;
            if (evenCircle)
            {
                angle = i * Mathf.PI * 2f / coinNum;   // evenly spaced
                dist = radius;
            }
            else
            {
                angle = Random.Range(0f, Mathf.PI * 2f); // random
                dist = Random.Range(radius * minRadiusPercent, radius);
            }

            // 3. Turn the angle into a position on the camera-facing circle
            Vector3 offset = (right * Mathf.Cos(angle) + up * Mathf.Sin(angle)) * dist;
            Vector3 burstPos = center.position + offset;

            // 4. Animate this coin
            StartCoroutine(AnimateCoin(coin, burstPos, i * flyStagger));
        }
    }

    IEnumerator AnimateCoin(Transform coin, Vector3 burstPos, float extraDelay)
    {
        // --- Phase 1: burst outward (fast start, slow end) ---
        Vector3 start = coin.position;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / burstDuration;
            coin.position = Vector3.Lerp(start, burstPos, EaseOutCubic(Mathf.Clamp01(t)));
            yield return null;
        }

        // --- Phase 2: short pause ---
        yield return new WaitForSeconds(pauseDuration + extraDelay);

        // --- Phase 3: fly to target (slow start, fast end) ---
        start = coin.position;
        t = 0f;
        while (t < 1f)
        {
            if (target == null) break; // safety: target was destroyed
            t += Time.deltaTime / flyDuration;
            coin.position = Vector3.Lerp(start, target.position, EaseInCubic(Mathf.Clamp01(t)));
            yield return null;
        }

        Destroy(coin.gameObject);
    }

    float EaseOutCubic(float x) => 1f - Mathf.Pow(1f - x, 3f);
    float EaseInCubic(float x) => x * x * x;
}