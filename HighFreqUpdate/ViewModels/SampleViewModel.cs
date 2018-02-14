﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using System.Windows;
using System.Windows.Threading;
using Catel;
using Catel.Collections;
using Catel.IoC;
using Catel.MVVM;
using Catel.Runtime.Serialization.Xml;
using Catel.Services;
using FizzWare.NBuilder;
using HighFreqUpdate.Models;
using Infragistics.Windows.DataPresenter;
using ServiceStack;

namespace HighFreqUpdate.ViewModels
{
    public class SampleViewModel : ViewModelBase
    {
        public FastObservableCollection<DealSpotVisual> DataItems { get; set; }
        private IDictionary<int, DealSpotVisual> mappingDummyItems { get; set; }

        public TaskCommand<XamDataGrid> SaveLayoutCommand { get; private set; }
        public TaskCommand<XamDataGrid> LoadLayoutCommand { get; private set; }

        private IDispatcherService dispatcherService;
        private object mappingDummyItemsLock = new object();
        private IDisposable IsGenerating()
        {
            return new DisposableToken<SampleViewModel>(this,
                x => x.Instance.IsBusy = true,
                x => x.Instance.IsBusy = false);
        }

        private bool IsBusy { get; set; }

        private bool hasToUpdate;
        private object hasToUpdateLock = new object();


        private Random random = new Random();


        private ConcurrentQueue<DealSpotVisual> queue = new ConcurrentQueue<DealSpotVisual>();
        private Timer timer;
        private DispatcherTimer dispatcherTimer = new DispatcherTimer();

        private const double dispatcherInterval = 250;
        private const string GridCustomizations = "c:\\temp\\xxx.xml";
        private const string GridLayout = "c:\\temp\\prova123.xml";

        public SampleViewModel(IDispatcherService dispatcherService)
        {
            this.dispatcherService = dispatcherService;

            SaveLayoutCommand = new TaskCommand<XamDataGrid>(OnSaveCommandExecute);
            LoadLayoutCommand = new TaskCommand<XamDataGrid>(OnLoadCommandExecute);

            dispatcherTimer.Interval = TimeSpan.FromMilliseconds(dispatcherInterval);
            dispatcherTimer.Tick += DispatcherTimer_Tick1;
            timer = new Timer(5000);
            timer.Elapsed += Timer_Elapsed;

            timer.Start();

            DataItems = new FastObservableCollection<DealSpotVisual>();

            lock (mappingDummyItemsLock)
            {
                mappingDummyItems = DataItems.ToDictionary(x => x.Id, x => x);
            }

        }

        private Task OnLoadCommandExecute(XamDataGrid grid)
        {

            timer.Stop();
            dispatcherTimer.Stop();

            var str = File.ReadAllText(GridLayout);

            byte[] bytes = Encoding.UTF8.GetBytes(str);

            return dispatcherService.InvokeAsync(() =>
            {
                using (var memoryStream = new MemoryStream(bytes))
                {
                    grid.LoadCustomizations(memoryStream);
                    timer.Start();
                    dispatcherTimer.Start();
                }


                //grid customizations
                var customizations = File.ReadAllText(GridCustomizations);

                byte[] bytesCustomizations = Encoding.UTF8.GetBytes(customizations);


                using (var ms = new MemoryStream(bytesCustomizations))
                {
                    IXmlSerializer x = ServiceLocator.Default.ResolveType<IXmlSerializer>();

                    GridCustomizations c = x.Deserialize(typeof(GridCustomizations), ms) as GridCustomizations;

                }
            });
        }




        private Task OnSaveCommandExecute(XamDataGrid grid)
        {
            timer.Stop();
            dispatcherTimer.Stop();

            return dispatcherService.InvokeAsync(() =>
            {
                using (MemoryStream memoryStream = new MemoryStream())
                {
                    grid.SaveCustomizations(memoryStream);

                    byte[] bytes = memoryStream.ToArray();

                    var str = Encoding.UTF8.GetString(bytes);

                    var externalInformations = GetGridExternalInformations(grid);

                    IXmlSerializer x = ServiceLocator.Default.ResolveType<IXmlSerializer>();

                    using (var ms = new MemoryStream())
                    {
                        x.Serialize(externalInformations, ms);

                        byte[] bytes2 = ms.ToArray();

                        var str1 = Encoding.UTF8.GetString(bytes2);

                        File.WriteAllText(GridCustomizations, str1);
                    }

                    File.WriteAllText(GridLayout, str);

                    timer.Start();
                    dispatcherTimer.Start();
                }
            });
        }

        public static GridCustomizations GetGridExternalInformations(XamDataGrid grid)
        {
            var gridCustomizations = new GridCustomizations();

            foreach (var field in grid.FieldLayouts[0].Fields)
            {
                //field.Name
                if (field.CellValuePresenterStyle?.Setters?.Count > 0)
                {
                    ColumnSettings columnSettings = new ColumnSettings();

                    foreach (Setter r in field.CellValuePresenterStyle.Setters)
                    {
                        if (r.Property.Name == Constants.ForegroundKey)
                        {
                            columnSettings.ForeColor = r.Value.ToString();
                        }
                        else if (r.Property.Name == Constants.BackgroundKey)
                        {
                            columnSettings.BackGroundColor = r.Value.ToString();
                        }
                    }

                    if (columnSettings.HasData)
                    {
                        gridCustomizations.ColumnsStyle[field.Name] = columnSettings;
                    }
                }

            }

            return gridCustomizations;
        }


        private void DispatcherTimer_Tick1(object sender, EventArgs e)
        {
            dispatcherTimer.Stop();

            if (hasToUpdate)
            {
                using (DataItems.SuspendChangeNotifications())
                {
                    while (queue.TryDequeue(out DealSpotVisual item))
                    {
                        if (mappingDummyItems.ContainsKey(item.Id))
                        {
                            mappingDummyItems[item.Id].PopulateWith(item);
                            mappingDummyItems[item.Id].IsChanged = true;
                        }
                        else
                        {
                            DataItems.Add(item);

                            lock (mappingDummyItemsLock)
                            {
                                mappingDummyItems = DataItems.ToDictionary(x => x.Id, x => x);
                            }
                        }
                    }
                }
            }
        }

        private void Timer_Elapsed(object sender, EventArgs e)
        {
            if (IsBusy) return;


            using (IsGenerating())
            {
                var priceGenerator = new RandomGenerator();
                var itemsToGenerate = random.Next(0, 20);

                for (int i = 0; i < itemsToGenerate; i++)
                {
                    var dummyItem = Builder<DealSpotVisual>.CreateNew()
                        .With(x => x.IdStatus = priceGenerator.Next(0, 4)).Build();

                    var index = random.Next(-20, 20);

                    dummyItem.Id = Math.Abs(index);

                    var cross = random.Next(1, 3);

                    dummyItem.IdCross = cross;
                    //dummyItem.Value = index * DateTime.Now.Ticks;

                    queue.Enqueue(dummyItem);
                }

                lock (hasToUpdateLock)
                {
                    hasToUpdate = true;
                }

                dispatcherTimer.Start();
            }
        }
    }
}


