using System;
using UnityEngine;

public interface ISelectableDrone
{
    bool IsSelected { get; }
    Sprite PortraitIcon { get; }
    GameObject GameObject { get; }

    event Action<ISelectableDrone> OnHealthChanged;
    event Action<ISelectableDrone> OnDied;

    void SetSelected(bool selected);
}
