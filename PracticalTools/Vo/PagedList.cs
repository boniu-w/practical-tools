using System.Collections.ObjectModel;
using System.ComponentModel;

namespace PracticalTools.vo;

public class PagedList<T> : ObservableCollection<T>, INotifyPropertyChanged
{
    public event PropertyChangedEventHandler PropertyChanged;
    protected void OnPropertyChanged(string name)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
    
    public ObservableCollection<T> SourceList { get; set; }
    private int _pageSize = 10;

    public int PageSize
    {
        get => _pageSize;
        set
        {
            if (value > 0)
            {
                _pageSize = value;
                GoToPage(1); // 重设到第一页
            }
        }
    }

    private int _currentPage = 1;
    public int CurrentPage
    {
        get => _currentPage;
        private set
        {
            if (_currentPage != value)
            {
                _currentPage = value;
                OnPropertyChanged(nameof(CurrentPage));
                OnPropertyChanged(nameof(PageCount)); // 可选
            }
        }
    }

    public int PageCount =>
        (int)Math.Ceiling((double)(SourceList?.Count ?? 0) / PageSize);

    public void Refresh()
    {
        this.Clear();
        if (SourceList == null) return;
        int start = (CurrentPage - 1) * PageSize;
        int end = Math.Min(start + PageSize, SourceList.Count);
        for (int i = start; i < end; i++)
            this.Add(SourceList[i]);
    }

    public void GoToPage(int page)
    {
        if (page < 1 || page > PageCount) return;
        CurrentPage = page;
        Refresh();
    }

    public void NextPage() => GoToPage(CurrentPage + 1);
    public void PreviousPage() => GoToPage(CurrentPage - 1);
    public void FirstPage() => GoToPage(1);
    public void LastPage() => GoToPage(PageCount);
}