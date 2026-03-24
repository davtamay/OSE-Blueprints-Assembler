using System;
using System.Numerics;
class P {
  static void Print(string name, Quaternion q) {
    var x = Vector3.Transform(Vector3.UnitX, q);
    var y = Vector3.Transform(Vector3.UnitY, q);
    var z = Vector3.Transform(Vector3.UnitZ, q);
    Console.WriteLine(name);
    Console.WriteLine($" X->{x}");
    Console.WriteLine($" Y->{y}");
    Console.WriteLine($" Z->{z}");
  }
  static void Main() {
    Print("left_top", new Quaternion(0f,0f,0.7071068f,0.7071068f));
    Print("left_side", new Quaternion(-0.5f,0.5f,0.5f,0.5f));
    Print("front_top", new Quaternion(-0.7071068f,0f,0f,0.7071068f));
    Print("front_side", new Quaternion(-0.5f,0.5f,-0.5f,0.5f));
    Print("top_side", new Quaternion(0f,0.7071068f,0f,0.7071068f));
  }
}
