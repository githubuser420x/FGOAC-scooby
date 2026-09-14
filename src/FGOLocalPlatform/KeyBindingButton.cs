using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace FGOLocalPlatform;

public sealed class KeyBindingButton : Button
{
	private int value;

	private bool listening;

	public int Value
	{
		get
		{
			return value;
		}
		set
		{
			this.value = value;
			listening = false;
			base.Content = KeyName(value);
		}
	}

	public KeyBindingButton()
	{
		SetResourceReference(FrameworkElement.StyleProperty, typeof(Button));
		base.HorizontalContentAlignment = HorizontalAlignment.Left;
		base.ToolTip = "Click, then press a key to bind it. Esc cancels; the right-click menu clears the binding.";
		ContextMenu contextMenu = new ContextMenu();
		MenuItem menuItem = new MenuItem
		{
			Header = "Clear Binding"
		};
		menuItem.Click += delegate
		{
			Value = 0;
		};
		contextMenu.Items.Add(menuItem);
		base.ContextMenu = contextMenu;
		base.LostKeyboardFocus += delegate
		{
			if (listening)
			{
				Value = value;
			}
		};
	}

	protected override void OnClick()
	{
		base.OnClick();
		Focus();
		listening = true;
		base.Content = "Press a key... (Esc cancels)";
	}

	protected override void OnPreviewKeyDown(KeyEventArgs e)
	{
		if (!listening)
		{
			base.OnPreviewKeyDown(e);
			return;
		}
		e.Handled = true;
		Key val = ((e.Key == Key.System) ? e.SystemKey : ((e.Key == Key.ImeProcessed) ? e.ImeProcessedKey : e.Key));
		if ((int)val == 13)
		{
			Value = value;
			return;
		}
		int num = KeyInterop.VirtualKeyFromKey(val);
		if (num > 0 && num <= 254)
		{
			Value = num;
		}
	}

	protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
	{
		if (!listening)
		{
			base.OnPreviewMouseDown(e);
			return;
		}
		e.Handled = true;
		Value = e.ChangedButton switch
		{
			MouseButton.Left => 1, 
			MouseButton.Right => 2, 
			MouseButton.Middle => 4, 
			MouseButton.XButton1 => 5, 
			MouseButton.XButton2 => 6, 
			_ => value, 
		};
	}

	public static string KeyName(int vk)
	{
		switch (vk)
		{
		case 0:
			return "Not bound";
		case 1:
			return "Left Mouse Button";
		case 2:
			return "Right Mouse Button";
		case 4:
			return "Middle Mouse Button";
		case 5:
			return "Mouse Button 4";
		case 6:
			return "Mouse Button 5";
		case 8:
			return "Backspace";
		case 9:
			return "Tab";
		case 13:
			return "Enter";
		case 20:
			return "Caps Lock";
		case 27:
			return "Esc";
		case 32:
			return "Space";
		case 16:
			return "Shift";
		case 17:
			return "Ctrl";
		case 18:
			return "Alt";
		case 160:
			return "Left Shift";
		case 161:
			return "Right Shift";
		case 162:
			return "Left Ctrl";
		case 163:
			return "Right Ctrl";
		case 164:
			return "Left Alt";
		case 165:
			return "Right Alt";
		case 33:
			return "Page Up";
		case 34:
			return "Page Down";
		case 35:
			return "End";
		case 36:
			return "Home";
		case 37:
			return "←";
		case 38:
			return "↑";
		case 39:
			return "→";
		case 40:
			return "↓";
		case 45:
			return "Insert";
		case 46:
			return "Delete";
		case 91:
			return "Left Win";
		case 92:
			return "Right Win";
		case 144:
			return "Num Lock";
		case 145:
			return "Scroll Lock";
		case 186:
			return ";";
		case 187:
			return "=";
		case 188:
			return ",";
		case 189:
			return "-";
		case 190:
			return ".";
		case 191:
			return "/";
		case 192:
			return "`";
		case 219:
			return "[";
		case 220:
			return "\\";
		case 221:
			return "]";
		case 222:
			return "'";
		case 48:
		case 49:
		case 50:
		case 51:
		case 52:
		case 53:
		case 54:
		case 55:
		case 56:
		case 57:
			return ((char)vk).ToString();
		case 65:
		case 66:
		case 67:
		case 68:
		case 69:
		case 70:
		case 71:
		case 72:
		case 73:
		case 74:
		case 75:
		case 76:
		case 77:
		case 78:
		case 79:
		case 80:
		case 81:
		case 82:
		case 83:
		case 84:
		case 85:
		case 86:
		case 87:
		case 88:
		case 89:
		case 90:
			return ((char)vk).ToString();
		case 96:
		case 97:
		case 98:
		case 99:
		case 100:
		case 101:
		case 102:
		case 103:
		case 104:
		case 105:
			return $"Numpad {vk - 96}";
		case 112:
		case 113:
		case 114:
		case 115:
		case 116:
		case 117:
		case 118:
		case 119:
		case 120:
		case 121:
		case 122:
		case 123:
		case 124:
		case 125:
		case 126:
		case 127:
		case 128:
		case 129:
		case 130:
		case 131:
		case 132:
		case 133:
		case 134:
		case 135:
			return $"F{vk - 111}";
		case 106:
			return "Numpad *";
		case 107:
			return "Numpad +";
		case 109:
			return "Numpad -";
		case 110:
			return "Numpad .";
		case 111:
			return "Numpad /";
		default:
			return ((int)KeyInterop.KeyFromVirtualKey(vk) == 0) ? $"Key 0x{vk:X}" : ((object)KeyInterop.KeyFromVirtualKey(vk)/*cast due to constrained. prefix*/).ToString();
		}
	}
}
