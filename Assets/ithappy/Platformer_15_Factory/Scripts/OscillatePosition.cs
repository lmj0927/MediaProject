using UnityEngine;
using System.Collections;

namespace ithappy.Platformer_15_Factory
{
    public class OscillatePosition : MonoBehaviour
    {
        public enum CurveType { Linear, EaseIn, EaseOut, EaseInOut, Sine, Bounce, Elastic, Quadratic, Quartic, Cubic, Quintic, Exponential, Circular, Back, PerlinNoise, Step }
        
        public Vector3 moveAxis = Vector3.up;
        public float moveDistance = 2f;
        public float duration = 2f;
        public bool useRandomDelay = false;
        public float maxRandomDelay = 1f;
        public bool useFixedDelay = false;
        public float fixedDelay = 0f;
        public float delayBetweenCycles = 0f;
        public CurveType curveType = CurveType.EaseInOut;
        public bool useLocalSpace = false;

        private Vector3 startPosition;
        private float timeElapsed = 0f;
        private float delayElapsed = 0f;
        private bool isReversing = false;
        private float randomDelay = 0f;

        void Start()
        {
            startPosition = useLocalSpace ? transform.localPosition : transform.position;

            if (useRandomDelay)
            {
                randomDelay = Random.Range(0f, maxRandomDelay);
            }
        }

        void Update()
        {
            if (useFixedDelay && delayElapsed < fixedDelay)
            {
                delayElapsed += Time.deltaTime;
                return;
            }

            if (timeElapsed < randomDelay)
            {
                timeElapsed += Time.deltaTime;
                return;
            }

            float progress = (timeElapsed - randomDelay) / (duration / 2f);
            progress = Mathf.Clamp01(progress);
            progress = ApplyCurve(progress);

            float currentDistance = moveDistance * (isReversing ? (1 - progress) : progress);
            Vector3 currentPosition = startPosition + moveAxis.normalized * currentDistance;

            if (useLocalSpace)
            {
                transform.localPosition = currentPosition;
            }
            else
            {
                transform.position = currentPosition;
            }

            timeElapsed += Time.deltaTime;

            if (timeElapsed >= duration / 2f + randomDelay)
            {
                timeElapsed = 0f;
                isReversing = !isReversing;

                StartCoroutine(CycleDelay());
            }
        }

        private IEnumerator CycleDelay()
        {
            yield return new WaitForSeconds(delayBetweenCycles);
        }

        private float ApplyCurve(float t)
        {
            switch (curveType)
            {
                case CurveType.Linear:
                    return t;
                case CurveType.EaseIn:
                    return t * t * t;
                case CurveType.EaseOut:
                    return 1 - Mathf.Pow(1 - t, 3);
                case CurveType.EaseInOut:
                    return t < 0.5f ? 4 * t * t * t : 1 - Mathf.Pow(-2 * t + 2, 3) / 2;
                case CurveType.Sine:
                    return Mathf.Sin(t * Mathf.PI * 0.5f);
                case CurveType.Bounce:
                    return Mathf.Abs(Mathf.Sin(6.28f * (t + 1) * (t + 1)) * (1 - t));
                case CurveType.Elastic:
                    return t == 0 ? 0 : t == 1 ? 1 : Mathf.Pow(2, -10 * t) * Mathf.Sin((t - 0.075f) * (2 * Mathf.PI) / 0.3f) + 1;
                case CurveType.Quadratic:
                    return t * t;
                case CurveType.Quartic:
                    return t * t * t * t;
                case CurveType.Cubic:
                    return t * t * t;
                case CurveType.Quintic:
                    return t * t * t * t * t;
                case CurveType.Exponential:
                    return t == 0 ? 0 : Mathf.Pow(2, 10 * (t - 1));
                case CurveType.Circular:
                    return 1 - Mathf.Sqrt(1 - t * t);
                case CurveType.Back:
                    float s = 1.70158f;
                    return t * t * ((s + 1) * t - s);
                case CurveType.PerlinNoise:
                    return Mathf.PerlinNoise(t * 10f, 0f);
                case CurveType.Step:
                    return t < 0.5f ? 0f : 1f;
                default:
                    return t;
            }
        }
    }
}
