using UnityEngine;
using System.Collections;

namespace ithappy.Platformer_15_Factory
{
    public class OscillateDualCurve : MonoBehaviour
    {
        public Vector3 rotationAxis = Vector3.up;
        public float rotationAngle = 45f;
        
        public AnimationCurve firstCycleCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
        public float firstCycleDuration = 1f;
        public float delayAfterFirstCycle = 0.5f;
        
        public AnimationCurve secondCycleCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
        public float secondCycleDuration = 1f;
        public float delayAfterSecondCycle = 0.5f;
        
        public bool useRandomStartDelay = false;
        public float maxRandomStartDelay = 1f;

        private Quaternion startRotation;
        private bool isReversing = false;

        void Start()
        {
            startRotation = transform.rotation;
            if (useRandomStartDelay)
            {
                float randomDelay = Random.Range(0f, maxRandomStartDelay);
                StartCoroutine(StartWithDelay(randomDelay));
            }
            else
            {
                StartCoroutine(AnimationLoop());
            }
        }

        private IEnumerator StartWithDelay(float delay)
        {
            yield return new WaitForSeconds(delay);
            StartCoroutine(AnimationLoop());
        }

        private IEnumerator AnimationLoop()
        {
            while (true)
            {
                yield return AnimateRotation(firstCycleCurve, firstCycleDuration, isReversing);
                yield return new WaitForSeconds(delayAfterFirstCycle);
                yield return AnimateRotation(secondCycleCurve, secondCycleDuration, !isReversing);
                yield return new WaitForSeconds(delayAfterSecondCycle);
            }
        }

        private IEnumerator AnimateRotation(AnimationCurve curve, float duration, bool reverse)
        {
            float timeElapsed = 0f;
            while (timeElapsed < duration)
            {
                float progress = timeElapsed / duration;
                float evaluatedProgress = curve.Evaluate(progress);
                
                float currentAngle = rotationAngle * (reverse ? (1 - evaluatedProgress) : evaluatedProgress);
                Quaternion currentRotation = startRotation * Quaternion.AngleAxis(currentAngle, rotationAxis);
                transform.rotation = currentRotation;

                timeElapsed += Time.deltaTime;
                yield return null;
            }
        }
    }
}
