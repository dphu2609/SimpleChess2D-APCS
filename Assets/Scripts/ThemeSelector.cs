using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class ThemeSelector : MonoBehaviour
{
    BoardUI boardUI;

    public Theme[] themes;

    private void Start(){
        boardUI = FindObjectOfType<BoardUI>();

        // Just use the first theme
        if (themes.Length > 0)
        {
            boardUI.theme = themes[0];
            boardUI.ResetAllSquareColors();
        }
    }
}
