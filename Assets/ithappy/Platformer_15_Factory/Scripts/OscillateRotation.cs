using UnityEngine;
using System.Collections;

namespace ithappy.Platformer_15_Factory
{
    public class OscillateRotation : MonoBehaviour
    {
        public enum CurveType { Linear, EaseIn, EaseOut, EaseInOut, Bounce, Elastic, SmoothStep, SmootherStep, Quadratic, Cubic, Quartic, Quintic, Sinusoidal, Exponential, Circular }
        public CurveType curveType = CurveType.EaseInOut;

        public Vector3 rotationAxis = Vector3.up;
        public float rotationAngle = 45f;
        public float duration = 2f;
        public bool useRandomDelay = false;
        public float maxRandomDelay = 1f;
        public bool useFixedDelay = false;
        public float fixedDelay = 0f;
        public float delayBetweenCycles = 0f;

        private Quaternion startRotation;
        private float timeElapsed = 0f;
        private float delayElapsed = 0f;
        private bool isReversing = false;
        private float randomDelay = 0f;

        void Start()
        {
            startRotation = transform.rotation;

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

            float currentAngle = rotationAngle * (isReversing ? (1 - progress) : progress);
            Quaternion currentRotation = startRotation * Quaternion.AngleAxis(currentAngle, rotationAxis);

            transform.rotation = currentRotation;

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
                case CurveType.Linear: return t;
                case CurveType.EaseIn: return t * t;
                case CurveType.EaseOut: return 1 - Mathf.Pow(1 - t, 2);
                case CurveType.EaseInOut: return t < 0.5f ? 4 * t * t * t : 1 - Mathf.Pow(-2 * t + 2, 3) / 2;
                case CurveType.Bounce: return Mathf.Abs(Mathf.Sin(6.28f * (t + 1) * (t + 1)) * (1 - t));
                case CurveType.Elastic: return Mathf.Sin(10 * t) * Mathf.Pow(2, -10 * t) + 1;
                case CurveType.SmoothStep: return t * t * (3 - 2 * t);
                case CurveType.SmootherStep: return t * t * t * (t * (t * 6 - 15) + 10);
                case CurveType.Quadratic: return t * t;
                case CurveType.Cubic: return t * t * t;
                case CurveType.Quartic: return t * t * t * t;
                case CurveType.Quintic: return t * t * t * t * t;
                case CurveType.Sinusoidal: return Mathf.Sin(t * Mathf.PI * 0.5f);
                case CurveType.Exponential: return t == 0 ? 0 : Mathf.Pow(2, 10 * (t - 1));
                case CurveType.Circular: return 1 - Mathf.Sqrt(1 - t * t);
                default: return t;
            }
        }
    }
}