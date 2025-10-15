using UnityEngine;
using UnityEngine.UI;

public class SoundManager : MonoBehaviour
{   
    public AudioSource bgmSource;
    public Slider bgmSlider;

    void Start()
    {
        if (bgmSlider != null)
        {
            bgmSlider.onValueChanged.AddListener(OnBgmSliderChanged);
            bgmSource.volume = bgmSlider.value;
        }
    }

    private void OnBgmSliderChanged(float value)
    {
        if (bgmSource != null)
            bgmSource.volume = value;
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
