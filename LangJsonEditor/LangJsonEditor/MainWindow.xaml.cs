using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text;


namespace LangJsonEditor
{
    public partial class MainWindow : Window
    {
        MainViewModel _vm = new();

		string? _currentFilePath = "";
		const double LengthWarningMultiplier = 1.0;
		List<string> _languageOrder = new();
		int _lastSearchIndex = -1;
		string _lastSearchText = "";


		public MainWindow()
        {
            InitializeComponent();
            DataContext = _vm;
		}

		static readonly JsonSerializerOptions JsonOptions = new()
		{
			Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
		};

		// ------------------------------------------------
		// BuildColumns
		// ------------------------------------------------
		void BuildColumns()
		{
			LangGrid.Columns.Clear();

			// Key column
			LangGrid.Columns.Add(new DataGridTextColumn
			{
				Header = "Key",
				Binding = new Binding("Key"),
				IsReadOnly = true,
				Width = new DataGridLength(350)
			});

			// Language columns in file order
			foreach (var lang in _languageOrder)
				AddLanguageColumn(lang);
		}

		// ------------------------------------------------
		// AddLanguageColumn
		// ------------------------------------------------
		void AddLanguageColumn(string lang)
		{
			var binding = new Binding($"Values[{lang}]")
			{
				Mode = BindingMode.TwoWay,
				UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
			};

			var style = new Style(typeof(DataGridCell));

			style.Setters.Add(new Setter(
				DataGridCell.BackgroundProperty,
				new Binding($"Values[{lang}]")
				{
					Converter = (IValueConverter)FindResource("EmptyCellBrush")
				}
			));

			LangGrid.Columns.Add(new DataGridTextColumn
			{
				Header = lang,
				Binding = binding,
				CellStyle = style,
				Width = new DataGridLength(1, DataGridLengthUnitType.Star)
			});
		}



		// ------------------------------------------------
		// LoadLangJson
		// ------------------------------------------------
		void LoadLangJson(string path)
		{
			string rawText = File.ReadAllText(path);
			string json = StripJsonComments(rawText);

			_vm.Entries.Clear();
			_languageOrder.Clear();

			using var doc = JsonDocument.Parse(json);

			bool languageOrderCaptured = false;

			foreach (var property in doc.RootElement.EnumerateObject())
			{
				var entry = new LangEntry
				{
					Key = property.Name
				};

				// FIRST entry defines language order
				if (!languageOrderCaptured)
				{
					foreach (var langProp in property.Value.EnumerateObject())
						_languageOrder.Add(langProp.Name);

					languageOrderCaptured = true;
				}

				foreach (var langProp in property.Value.EnumerateObject())
					entry.Values[langProp.Name] = langProp.Value.GetString() ?? "";

				_vm.Entries.Add(entry);
			}

			BuildColumns();
			UpdateStatusBar();
		}

		// ------------------------------------------------
		// StripJsonComments
		// ------------------------------------------------
		static string StripJsonComments(string input)
		{
			var sb = new StringBuilder(input.Length);

			bool inString = false;
			bool escape = false;

			for (int i = 0; i < input.Length; i++)
			{
				char c = input[i];
				char next = i + 1 < input.Length ? input[i + 1] : '\0';

				if (escape)
				{
					sb.Append(c);
					escape = false;
					continue;
				}

				if (c == '\\')
				{
					sb.Append(c);
					escape = true;
					continue;
				}

				if (c == '"')
				{
					sb.Append(c);
					inString = !inString;
					continue;
				}

				// line comment
				if (!inString && c == '/' && next == '/')
				{
					// skip until end of line
					while (i < input.Length && input[i] != '\n')
						i++;
					sb.Append('\n');
					continue;
				}

				sb.Append(c);
			}

			return sb.ToString();
		}

		// ------------------------------------------------
		// SaveLangJson
		// ------------------------------------------------
		void SaveLangJson(string path)
		{
			var sb = new StringBuilder();
			sb.AppendLine("{");

			for (int i = 0; i < _vm.Entries.Count; i++)
			{
				var entry = _vm.Entries[i];

				sb.AppendLine($"  \"{entry.Key}\": {{");

				int j = 0;
				foreach (var kv in entry.Values)
				{
					string comma = j < entry.Values.Count - 1 ? "," : "";
					sb.AppendLine($"    \"{kv.Key}\": {JsonSerializer.Serialize(kv.Value, JsonOptions)}{comma}");
					j++;
				}

				string endComma = i < _vm.Entries.Count - 1 ? "," : "";
				sb.AppendLine($"  }}{endComma}");
			}

			sb.AppendLine("}");

			File.WriteAllText(path, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
		}

		// ------------------------------------------------
		// PromptForLanguageCode
		// ------------------------------------------------
		string? PromptForLanguageCode()
		{
			var window = new Window
			{
				Title = "Add Language",
				Width = 300,
				Height = 140,
				WindowStartupLocation = WindowStartupLocation.CenterOwner,
				ResizeMode = ResizeMode.NoResize,
				Owner = this
			};

			var panel = new StackPanel { Margin = new Thickness(10) };

			panel.Children.Add(new TextBlock
			{
				Text = "Language code (e.g. en, fr, pt, ja):"
			});

			var textBox = new TextBox { Margin = new Thickness(0, 5, 0, 10) };
			panel.Children.Add(textBox);

			var buttons = new StackPanel
			{
				Orientation = Orientation.Horizontal,
				HorizontalAlignment = HorizontalAlignment.Right
			};

			var ok = new Button { Content = "OK", Width = 70, IsDefault = true };
			var cancel = new Button { Content = "Cancel", Width = 70, Margin = new Thickness(5, 0, 0, 0), IsCancel = true };

			buttons.Children.Add(ok);
			buttons.Children.Add(cancel);

			panel.Children.Add(buttons);

			window.Content = panel;

			ok.Click += (_, __) => window.DialogResult = true;

			return window.ShowDialog() == true
				? textBox.Text.Trim()
				: null;
		}

		// ------------------------------------------------
		// OnAddLanguage
		// ------------------------------------------------
		void OnAddLanguage(object sender, RoutedEventArgs e)
		{
			var lang = PromptForLanguageCode();
			if (string.IsNullOrWhiteSpace(lang))
				return;

			// normalise
			lang = lang.Trim().ToLowerInvariant();

			// check if column already exists
			foreach (var col in LangGrid.Columns)
			{
				if (col.Header?.ToString() == lang)
				{
					MessageBox.Show($"Language '{lang}' already exists.");
					return;
				}
			}

			// add to all entries
			foreach (var entry in _vm.Entries)
			{
				if (!entry.Values.ContainsKey(lang))
					entry.Values[lang] = "";
			}

			// add column
			AddLanguageColumn(lang);

			UpdateStatusBar();
		}

		// ------------------------------------------------
		// OnMergePaste
		// ------------------------------------------------
		void OnMergePaste(object sender, RoutedEventArgs e)
		{
			var text = PasteBox.Text;
			if (string.IsNullOrWhiteSpace(text))
				return;

			try
			{
				MergeJsonBlock(text);
				PasteBox.Clear();
			}
			catch (Exception ex)
			{
				MessageBox.Show("Paste failed:\n\n" + ex.Message);
			}
		}

		// ------------------------------------------------
		// MergeJsonBlock
		// ------------------------------------------------
		void MergeJsonBlock(string pastedText)
		{
			string json = pastedText.Trim();
			if (!json.StartsWith("{"))
				json = "{\n" + json + "\n}";

			using var doc = JsonDocument.Parse(json);

			// existing languages
			var existingLanguages = new HashSet<string>(
				LangGrid.Columns
					.Select(c => c.Header?.ToString())
					.Where(h => !string.IsNullOrWhiteSpace(h) && h != "Key")
			);

			// validate languages first
			foreach (var prop in doc.RootElement.EnumerateObject())
			{
				foreach (var langProp in prop.Value.EnumerateObject())
				{
					if (!existingLanguages.Contains(langProp.Name))
					{
						throw new Exception(
							$"Language '{langProp.Name}' has not been added yet.\n\n" +
							$"Please add it first via File → Add Language."
						);
					}
				}
			}

			int updated = 0;
			int missingKeys = 0;

			// NEW: collect length warnings
			var lengthWarnings = new List<string>();

			foreach (var prop in doc.RootElement.EnumerateObject())
			{
				var entry = _vm.Entries.FirstOrDefault(e => e.Key == prop.Name);
				if (entry == null)
				{
					missingKeys++;
					continue;
				}

				// compute longest existing translation for this key
				int longestExisting = entry.Values.Values
					.Where(v => !string.IsNullOrEmpty(v))
					.Select(v => v.Length)
					.DefaultIfEmpty(0)
					.Max();

				foreach (var langProp in prop.Value.EnumerateObject())
				{
					string newValue = langProp.Value.GetString() ?? "";
					int newLength = newValue.Length;

					if (longestExisting > 0 &&
						newLength > longestExisting * LengthWarningMultiplier)
					{
						lengthWarnings.Add(
							$"{entry.Key} [{langProp.Name}]: {newLength} vs {longestExisting}"
						);
					}

					entry.Values[langProp.Name] = newValue;
				}

				updated++;
			}

			UpdateStatusBar();
			LangGrid.Items.Refresh();

			// build result message
			var sb = new StringBuilder();
			sb.AppendLine("Merge complete.");
			sb.AppendLine();
			sb.AppendLine($"Updated entries: {updated}");
			sb.AppendLine($"Unknown keys ignored: {missingKeys}");

			if (lengthWarnings.Count > 0)
			{
				sb.AppendLine();
				sb.AppendLine("⚠ Possible length issues detected:");
				sb.AppendLine();

				// limit spam
				foreach (var w in lengthWarnings.Take(10))
					sb.AppendLine("• " + w);

				if (lengthWarnings.Count > 10)
					sb.AppendLine($"• …and {lengthWarnings.Count - 10} more");

				sb.AppendLine();
				sb.AppendLine("You may want to check these in-game.");
			}

			MessageBox.Show(sb.ToString());
		}


		// ------------------------------------------------
		// OnCopyCanExecute
		// ------------------------------------------------
		void OnCopyCanExecute(object sender, CanExecuteRoutedEventArgs e)
		{
			// Enable Ctrl+C only if at least one row is selected
			e.CanExecute = LangGrid.SelectedItems.Count > 0;
			e.Handled = true;
		}

		void OnCopyCommand(object sender, ExecutedRoutedEventArgs e)
		{
			var selected = LangGrid.SelectedItems
				.OfType<LangEntry>()
				.ToList();

			if (selected.Count == 0)
				return;

			string json = BuildJsonForEntries(selected);

			Clipboard.SetText(json);

			// Optional feedback (comment out if it feels noisy)
			// MessageBox.Show($"Copied {selected.Count} entries as JSON.");

			e.Handled = true;
		}

		// ------------------------------------------------
		// BuildJsonForEntries
		// ------------------------------------------------
		string BuildJsonForEntries(List<LangEntry> entries)
		{
			var sb = new StringBuilder();
			sb.AppendLine("{");

			for (int i = 0; i < entries.Count; i++)
			{
				var entry = entries[i];
				sb.AppendLine($"  \"{entry.Key}\": {{");

				int j = 0;
				foreach (var kv in entry.Values)
				{
					string comma = j < entry.Values.Count - 1 ? "," : "";
					sb.AppendLine(
						$"    \"{kv.Key}\": {JsonSerializer.Serialize(kv.Value, JsonOptions)}{comma}"
					);
					j++;
				}

				string endComma = i < entries.Count - 1 ? "," : "";
				sb.AppendLine($"  }}{endComma}");
			}

			sb.AppendLine("}");
			return sb.ToString();
		}

		// ------------------------------------------------
		// OnOpenFile
		// ------------------------------------------------
		void OnOpenFile(object sender, RoutedEventArgs e)
		{
			var dlg = new Microsoft.Win32.OpenFileDialog
			{
				Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*"
			};

			if (dlg.ShowDialog() == true)
			{
				_currentFilePath = dlg.FileName;
				LoadLangJson(_currentFilePath);
				Title = $"Lang JSON Editor — {System.IO.Path.GetFileName(_currentFilePath)}";
			}

			UpdateStatusBar();
		}

		// ------------------------------------------------
		// OnSaveFile
		// ------------------------------------------------
		void OnSaveFile(object sender, RoutedEventArgs e)
		{
			if (_currentFilePath == null)
			{
				OnSaveAsFile(sender, e);
				return;
			}

			SaveLangJson(_currentFilePath);
		}

		// ------------------------------------------------
		// OnSaveAsFile
		// ------------------------------------------------
		void OnSaveAsFile(object sender, RoutedEventArgs e)
		{
			var dlg = new Microsoft.Win32.SaveFileDialog
			{
				Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
				FileName = _currentFilePath != null
					? System.IO.Path.GetFileName(_currentFilePath)
					: "lang.json"
			};

			if (dlg.ShowDialog() == true)
			{
				_currentFilePath = dlg.FileName;
				SaveLangJson(_currentFilePath);
				Title = $"Lang JSON Editor — {System.IO.Path.GetFileName(_currentFilePath)}";
			}
		}

		// ------------------------------------------------
		// UpdateStatusBar
		// ------------------------------------------------
		void UpdateStatusBar()
		{
			int total = _vm.Entries.Count;

			// all known languages (from columns)
			var languages = LangGrid.Columns
				.Select(c => c.Header?.ToString())
				.Where(h => !string.IsNullOrWhiteSpace(h) && h != "Key")
				.ToList();

			int missingAny = 0;
			var missingPerLanguage = new Dictionary<string, int>();

			foreach (var lang in languages)
				missingPerLanguage[lang] = 0;

			foreach (var entry in _vm.Entries)
			{
				bool entryMissingAny = false;

				foreach (var lang in languages)
				{
					if (!entry.Values.TryGetValue(lang, out var value) ||
						string.IsNullOrWhiteSpace(value))
					{
						missingPerLanguage[lang]++;
						entryMissingAny = true;
					}
				}

				if (entryMissingAny)
					missingAny++;
			}

			// pick a “primary” language to show (last added or first non-en)
			string? primaryLang =
				languages.FirstOrDefault(l => l != "en") ?? languages.FirstOrDefault();

			string primaryText = primaryLang != null
				? $"Missing ({primaryLang}): {missingPerLanguage[primaryLang]}"
				: "Missing: 0";

			StatusText.Text =
				$"Entries: {total}   |   Missing (any): {missingAny}";
		}

		// ------------------------------------------------
		// OnWindowDragOver
		// ------------------------------------------------
		void OnWindowDragOver(object sender, DragEventArgs e)
		{
			if (e.Data.GetDataPresent(DataFormats.FileDrop))
			{
				var files = (string[])e.Data.GetData(DataFormats.FileDrop);

				if (files.Length == 1 && files[0].EndsWith(".json", StringComparison.OrdinalIgnoreCase))
				{
					e.Effects = DragDropEffects.Copy;
				}
				else
				{
					e.Effects = DragDropEffects.None;
				}
			}
			else
			{
				e.Effects = DragDropEffects.None;
			}

			e.Handled = true;
		}

		// ------------------------------------------------
		// OnWindowDrop
		// ------------------------------------------------
		void OnWindowDrop(object sender, DragEventArgs e)
		{
			if (!e.Data.GetDataPresent(DataFormats.FileDrop))
				return;

			var files = (string[])e.Data.GetData(DataFormats.FileDrop);
			if (files.Length != 1)
				return;

			var path = files[0];
			if (!path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
				return;

			try
			{
				_currentFilePath = path;
				LoadLangJson(path);
				Title = $"Lang JSON Editor — {System.IO.Path.GetFileName(path)}";
			}
			catch (Exception ex)
			{
				MessageBox.Show(
					"Failed to open file:\n\n" + ex.Message,
					"Error",
					MessageBoxButton.OK,
					MessageBoxImage.Error
				);
			}
		}

		// ------------------------------------------------
		// OnSearchKeyDown
		// ------------------------------------------------
		void OnSearchKeyDown(object sender, KeyEventArgs e)
		{
			if (e.Key == Key.Enter)
			{
				PerformSearch(SearchBox.Text);
				e.Handled = true;
			}
		}

		// ------------------------------------------------
		// PerformSearch
		// ------------------------------------------------
		void PerformSearch(string text)
		{
			if (string.IsNullOrWhiteSpace(text))
				return;

			text = text.Trim();

			// Reset cycling if text changed
			if (!string.Equals(text, _lastSearchText, StringComparison.OrdinalIgnoreCase))
			{
				_lastSearchIndex = -1;
				_lastSearchText = text;
			}

			for (int i = _lastSearchIndex + 1; i < _vm.Entries.Count; i++)
			{
				var entry = _vm.Entries[i];

				if (entry.Key.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)
				{
					SelectAndScrollToRow(i);
					_lastSearchIndex = i;
					return;
				}
			}

			// Wrap around
			for (int i = 0; i <= _lastSearchIndex; i++)
			{
				var entry = _vm.Entries[i];

				if (entry.Key.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)
				{
					SelectAndScrollToRow(i);
					_lastSearchIndex = i;
					return;
				}
			}

			// Nothing found
			System.Media.SystemSounds.Beep.Play();
		}

		// ------------------------------------------------
		// SelectAndScrollToRow
		// ------------------------------------------------
		void SelectAndScrollToRow(int index)
		{
			var entry = _vm.Entries[index];

			LangGrid.SelectedItems.Clear();
			LangGrid.SelectedItem = entry;
			LangGrid.ScrollIntoView(entry);

			LangGrid.Focus();
		}



	}
}