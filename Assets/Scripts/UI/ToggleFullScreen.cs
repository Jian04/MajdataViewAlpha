using UnityEngine;
using UnityEngine.UI;

public class ToggleFullScreen : MonoBehaviour
{
    private Dropdown dd;
    private int windowedWidth = 512;
    private int windowedHeight = 512;

    public void Start()
    {
        dd = GameObject.Find("ResoDropdown").GetComponent<Dropdown>();
        dd.gameObject.SetActive(false);
        if (!Screen.fullScreen)
        {
            windowedWidth = Screen.width;
            windowedHeight = Screen.height;
        }
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape)) ToggleFullscreen();
    }

    public void ToggleFullscreen()
    {
        print("ToggleFullScreen");
        var resolutions = Screen.resolutions;
        if (Screen.fullScreen)
        {
            Screen.fullScreenMode = FullScreenMode.Windowed;
            Screen.SetResolution(Mathf.Max(320, windowedWidth), Mathf.Max(320, windowedHeight), false);
        }
        else
        {
            windowedWidth = Screen.width;
            windowedHeight = Screen.height;
            var target = resolutions.Length > 0
                ? resolutions[resolutions.Length - 1]
                : Screen.currentResolution;
            Screen.fullScreenMode = FullScreenMode.FullScreenWindow;
            Screen.SetResolution(target.width, target.height, true);
        }
    }

    public void DisplayDropdown()
    {
        dd.value = 999;
        dd.gameObject.SetActive(true);
    }

    public void SetResolution()
    {
        var i = dd.value;
        print(i);
        var width = Screen.width;
        var height = Screen.height;
        switch (i)
        {
            case 0:
                width = 512;
                height = 512;
                break;
            case 1:
                width = 1080;
                height = 1080;
                break;
            case 2:
                width = 1280;
                height = 720;
                break;
            case 3:
                width = 1920;
                height = 1080;
                break;
            case 4:
                width = 2560;
                height = 1440;
                break;
            case 5:
                width = 3840;
                height = 2160;
                break;
        }

        windowedWidth = width;
        windowedHeight = height;
        Screen.SetResolution(width, height, false);

        dd.gameObject.SetActive(false);
    }
}
