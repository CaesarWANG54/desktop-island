using System.Windows;
using System.Windows.Media;
using DesktopIsland.Core;

namespace DesktopIsland.UI;

public sealed class WeatherIcon : FrameworkElement
{
    private int _code=-1;private bool _day=true;
    public WeatherIcon(){Width=24;Height=24;}
    public void Set(WeatherReading? reading)
    {
        int code=reading?.Code??-1;bool day=reading?.IsDay??true;
        if(code==_code&&day==_day)return;_code=code;_day=day;InvalidateVisual();
    }
    protected override void OnRender(DrawingContext dc)
    {
        dc.PushTransform(new ScaleTransform(ActualWidth/24,ActualHeight/24));
        Brush cloud=new SolidColorBrush(Color.FromRgb(201,209,222)),yellow=new SolidColorBrush(Color.FromRgb(248,206,103)),blue=new SolidColorBrush(Color.FromRgb(125,191,251));
        Pen Pen(Brush color,double width=1.7)=>new(color,width){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round};
        void Line(double x,double y,double x2,double y2,Brush color)=>dc.DrawLine(Pen(color),new(x,y),new(x2,y2));
        void Sun(double x,double y,double radius)
        {
            dc.DrawEllipse(null,Pen(yellow),new(x,y),radius,radius);
            for(int i=0;i<8;i++){double a=i*Math.PI/4;Line(x+Math.Cos(a)*(radius+2),y+Math.Sin(a)*(radius+2),x+Math.Cos(a)*(radius+3.5),y+Math.Sin(a)*(radius+3.5),yellow);}
        }
        void Cloud()=>dc.DrawGeometry(new SolidColorBrush(Color.FromRgb(41,47,59)),Pen(cloud),Geometry.Parse("M5,17 C1,17 1,11 5,10 C5,4 14,3 16,9 C22,8 24,17 18,17 Z"));
        if(_code==0)
        {
            if(_day)Sun(12,12,4);
            else dc.DrawGeometry(null,Pen(yellow),Geometry.Parse("M16,3 C7,1 2,11 7,17 C12,23 21,18 21,13 C13,16 8,8 16,3 Z"));
        }
        else if(_code is 1 or 2){Sun(8,7,2.6);Cloud();}
        else if(_code==3)Cloud();
        else if(_code is 45 or 48){Cloud();Line(3,20,18,20,cloud);Line(7,23,21,23,cloud);}
        else if(_code is 51 or 53 or 55 or 56 or 57 or 61 or 63 or 65 or 66 or 67 or 80 or 81 or 82)
        {Cloud();Line(7,19,5,22,blue);Line(13,19,11,22,blue);Line(19,19,17,22,blue);}
        else if(_code is 71 or 73 or 75 or 77 or 85 or 86)
        {Cloud();foreach(double x in new[]{7d,17d}){Line(x-2,21,x+2,21,cloud);Line(x,19,x,23,cloud);}}
        else if(_code is 95 or 96 or 99){Cloud();dc.DrawGeometry(yellow,null,Geometry.Parse("M13,14 L9,20 L13,20 L11,24 L18,17 L14,17 L16,14 Z"));}
        else {Line(7,12,10,12,cloud);Line(14,12,17,12,cloud);}
        dc.Pop();
    }
}
