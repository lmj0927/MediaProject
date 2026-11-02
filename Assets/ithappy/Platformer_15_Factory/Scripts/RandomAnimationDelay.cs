namespace ithappy.Platformer_15_Factory
{
using UnityEngine;

public class RandomAnimationDelay : MonoBehaviour
{
    public Animator animator;  // Ссылка на Animator
    public string animationTrigger = "PlayAnimation";  // Имя триггера анимации
    public float minDelay = 0.5f;  // Минимальная задержка
    public float maxDelay = 2f;    // Максимальная задержка

    private void Start()
    {
        // Запускаем задержку при старте
        StartCoroutine(PlayAnimationWithRandomDelay());
    }

    private System.Collections.IEnumerator PlayAnimationWithRandomDelay()
    {
        // Вычисляем случайную задержку
        float delay = Random.Range(minDelay, maxDelay);

        // Ждем случайное время
        yield return new WaitForSeconds(delay);

        // Включаем анимацию
        animator.SetTrigger(animationTrigger);
    }
}

}