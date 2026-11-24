using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using Microsoft.Win32;

namespace ServiceDeskWpf;

internal record Ticket(int Id, string Item, string Owner, DateTime Due, string Status);

internal static class TestData
{
    public static List<Ticket> Create(DateTime today) =>
    [
        new Ticket(101, "Автобус А", "Механик А", today.AddDays(-2), "Открыта"),
        new Ticket(102, "Автобус Б", "Механик Б", today.AddDays(-1), "Завершена")
    ];

    public static int CountOverdue(IEnumerable<Ticket> tickets, DateTime today) =>
        tickets.Count(t => t.Status != "Завершена" && t.Due.Date < today.Date);
}

internal static class CsvStore
{
    public static List<Ticket> Read(string path, bool useColumnNames, out List<string> warnings)
    {
        warnings = new List<string>();
        var rows = new List<Ticket>();
        var lines = File.ReadAllLines(path, Encoding.UTF8);
        if (lines.Length == 0) return rows;

        var header = lines[0].Split(';');
        int idxId = 0, idxItem = 1, idxOwner = 2, idxDue = 3, idxStatus = 4;

        // Если включен режим имен столбцов, ищем их позиции
        if (useColumnNames)
        {
            var knownCols = new[] { "Id", "Item", "Owner", "Due", "Status" };
            if (header.Length != 5 || header.Distinct().Count() != 5 || !header.All(h => knownCols.Contains(h)))
                throw new Exception("Неверный заголовок: обнаружены неизвестные или повторяющиеся столбцы");

            idxId = Array.IndexOf(header, "Id");
            idxItem = Array.IndexOf(header, "Item");
            idxOwner = Array.IndexOf(header, "Owner");
            idxDue = Array.IndexOf(header, "Due");
            idxStatus = Array.IndexOf(header, "Status");
        }
        else
        {
            // Канонический порядок
            if (header[0] != "Id" || header[1] != "Item" || header[2] != "Owner" || header[3] != "Due" || header[4] != "Status")
                throw new Exception("Неверный заголовок: ожидается Id;Item;Owner;Due;Status");
        }

        for (int i = 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line)) continue;
            var cells = line.Split(';');
            if (cells.Length < 5)
            {
                warnings.Add($"Строка {i + 1}: неполная запись");
                continue;
            }

            try
            {
                var id = int.Parse(cells[idxId]);
                var item = cells[idxItem];
                var owner = cells[idxOwner];
                var due = DateTime.ParseExact(cells[idxDue], "dd.MM.yyyy", CultureInfo.InvariantCulture);
                var status = cells[idxStatus];
                if (status != "Открыта" && status != "Завершена")
                {
                    warnings.Add($"Строка {i + 1}: неизвестный статус '{status}'");
                    continue;
                }
                rows.Add(new Ticket(id, item, owner, due, status));
            }
            catch (Exception ex)
            {
                warnings.Add($"Строка {i + 1}: {ex.Message}");
            }
        }
        return rows;
    }

    public static void Write(string path, IEnumerable<Ticket> rows)
    {
        var lines = new List<string> { "Id;Item;Owner;Due;Status" };
        lines.AddRange(rows.Select(t => $"{t.Id};{t.Item};{t.Owner};{t.Due:dd.MM.yyyy};{t.Status}"));
        File.WriteAllLines(path, lines, Encoding.UTF8);
    }

    public static int CountOverdue(IEnumerable<Ticket> rows, DateTime today) =>
        rows.Count(t => t.Status == "Открыта" && t.Due.Date < today.Date);
}

public partial class MainWindow : Window
{
    private List<Ticket> tickets = new();
    private DateTime CheckDate => ControlDate.SelectedDate!.Value.Date;

    public MainWindow(string? demoPath = null, DateTime? checkDate = null)
    {
        InitializeComponent();
        ControlDate.SelectedDate = checkDate ?? new DateTime(2026, 10, 1);
        if (demoPath != null) Loaded += (_, _) =>
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle,
                new Action(() => LoadCsv(demoPath)));
    }

    private void ControlDate_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (ControlDate.SelectedDate is null)
            ControlDate.SelectedDate = new DateTime(2026, 10, 1);
    }

    private void LoadTestData_Click(object sender, RoutedEventArgs e) => LoadTestData();

    private void LoadTestData()
    {
        tickets = TestData.Create(CheckDate);
        TicketsGrid.ItemsSource = tickets.ToList();
        StatusText.Text = $"Тестовых записей: {tickets.Count}";
    }

    private void OpenCsv_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "CSV (*.csv)|*.csv" };
        if (dialog.ShowDialog() == true) LoadCsv(dialog.FileName);
    }

    private void LoadCsv(string path)
    {
        try
        {
            bool useNames = UseColumnNamesCheck.IsChecked == true;
            tickets = CsvStore.Read(path, useNames, out var warnings);

            if (tickets.Count == 0)
            {
                TicketsGrid.ItemsSource = null;
                StatusText.Text = warnings.Count > 0
                    ? $"Ошибка импорта: {string.Join("; ", warnings)}"
                    : "В файле нет данных";
            }
            else
            {
                TicketsGrid.ItemsSource = tickets.ToList();
                StatusText.Text = warnings.Count > 0
                    ? $"Загружено: {tickets.Count}. Предупреждения: {string.Join("; ", warnings)}"
                    : $"Загружено: {tickets.Count}";
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Ошибка импорта: {ex.Message}";
            MessageBox.Show(ex.Message, "Ошибка импорта");
        }
    }
    private void SaveCsv_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = "export.csv" };
        if (dialog.ShowDialog() == true)
        {
            try { CsvStore.Write(dialog.FileName, tickets); StatusText.Text = "Файл сохранён"; }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Ошибка экспорта"); }
        }
    }

    private void CountOverdue_Click(object sender, RoutedEventArgs e) =>
        StatusText.Text = $"Просрочено: {CsvStore.CountOverdue(tickets, CheckDate)}";
    private void PriorityInspection_Click(object sender, RoutedEventArgs e)
    {
        // Отбираем только открытые записи
        var openTickets = tickets.Where(t => t.Status == "Открыта").ToList();

        // Сортируем: сначала по дате (раньше — выше), потом по Id (меньше — выше)
        var priority = openTickets
            .OrderBy(t => t.Due.Date)
            .ThenBy(t => t.Id)
            .FirstOrDefault();

        if (priority != null)
        {
            // Показываем только одну запись
            TicketsGrid.ItemsSource = new List<Ticket> { priority };
            StatusText.Text = $"Первоочередной осмотр: Id {priority.Id} ({priority.Item})";
        }
        else
        {
            TicketsGrid.ItemsSource = new List<Ticket>();
            StatusText.Text = "Нет открытых записей для осмотра";
        }
    }
}
