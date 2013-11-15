﻿Imports System.Collections.ObjectModel
Imports System.Web.Script.Serialization
Imports System.Reflection
Imports kintoneDotNET.API.Types

Namespace API

    ''' <summary>
    ''' kintoneから返却されるJSONの読み込み、また送信する際のシリアライズを行うConvertor
    ''' </summary>
    ''' <typeparam name="T"></typeparam>
    ''' <remarks></remarks>
    Public Class kintoneContentConvertor(Of T As AbskintoneModel)
        Inherits JavaScriptConverter

        Private _serializer As New JavaScriptSerializer

        Public Overrides ReadOnly Property SupportedTypes As IEnumerable(Of Type)
            Get
                Return New ReadOnlyCollection(Of Type)(New List(Of Type) From {GetType(T)})
            End Get
        End Property

        ''' <summary>
        ''' kintoneへ送信する際のシリアライズを行う
        ''' kintoneへ送信するのは、UploadTargetAttribute属性がついている項目のみ
        ''' </summary>
        ''' <param name="obj"></param>
        ''' <param name="serializer"></param>
        ''' <returns></returns>
        ''' <remarks></remarks>
        Public Overrides Function Serialize(obj As Object, serializer As JavaScriptSerializer) As IDictionary(Of String, Object)

            Dim result As New Dictionary(Of String, Object)

            'UploadTargetAttributeがセットされている項目を取得してシリアライズ
            Dim objType As Type = obj.GetType
            Dim props As PropertyInfo() = objType.GetProperties()
            Dim targets = From p As PropertyInfo In props
                          Let attribute As UploadTargetAttribute = p.GetCustomAttributes(GetType(UploadTargetAttribute), True).SingleOrDefault
                          Where Not attribute Is Nothing
                          Select p, attribute

            For Each tgt In targets
                Dim value As Object = tgt.p.GetValue(obj, Nothing)
                Dim pType As Type = getGenericsType(tgt.p.PropertyType)

                If TypeOf value Is IList Then
                    Dim list As New List(Of Object)
                    For Each v In value
                        list.Add(makeKintoneItem(pType, tgt.attribute, v, serializer, True))
                    Next
                    result.Add(tgt.p.Name, New With {.value = list})
                Else
                    result.Add(tgt.p.Name, makeKintoneItem(pType, tgt.attribute, value, serializer))
                End If
            Next

            Return result

        End Function

        ''' <summary>
        ''' Serialize時、項目の型に応じた変換処理を行う
        ''' </summary>
        ''' <param name="pType"></param>
        ''' <param name="value"></param>
        ''' <returns></returns>
        ''' <remarks></remarks>
        Private Function makeKintoneItem(ByVal pType As Type, ByVal attr As UploadTargetAttribute, ByVal value As Object, ByVal serializer As JavaScriptSerializer, Optional ByVal isRaw As Boolean = False) As Object
            Dim result As Object = Nothing

            If pType.BaseType = GetType(kintoneSubTableItem) Then '内部テーブル項目
                Dim id As String = CType(value, kintoneSubTableItem).id
                Dim tableData As IDictionary(Of String, Object) = Serialize(value, serializer)
                If tableData.ContainsKey("id") Then
                    tableData.Remove("id")
                    If Not String.IsNullOrEmpty(id) Then
                        result = New With {.id = id, .value = tableData}
                    Else
                        result = New With {.value = tableData}
                    End If
                End If
            Else

                Select Case pType
                    Case GetType(DateTime)
                        Dim d As DateTime = value
                        If value Is Nothing Then
                            d = kintoneDatetime.InitialValue
                        End If
                        result = kintoneDatetime.toKintoneDate(d, attr.FieldType)
                    Case GetType(kintoneFile)
                        Dim obj As kintoneFile = CType(value, kintoneFile)
                        result = New With {.fileKey = obj.fileKey}
                    Case Else
                        If (value Is Nothing OrElse String.IsNullOrEmpty(value.ToString)) And _
                            Not attr.InitialValue Is Nothing Then
                            result = attr.InitialValue
                        Else
                            result = value
                        End If
                End Select

            End If

            If isRaw Then
                Return result
            Else
                Return New With {.value = result} 'kintoneで値を送る場合、value:"xxxx"という形にする
            End If

        End Function

        ''' <summary>
        ''' kintoneから受け取ったJSONを、モデル型配列に変換する
        ''' </summary>
        ''' <param name="dictionary"></param>
        ''' <param name="type"></param>
        ''' <param name="serializer"></param>
        ''' <returns></returns>
        ''' <remarks></remarks>
        Public Overrides Function Deserialize(dictionary As IDictionary(Of String, Object), type As Type, serializer As JavaScriptSerializer) As Object
            Dim objType As Type = type
            Dim m = Activator.CreateInstance(objType)

            'デフォルトプロパティ格納
            For Each item As KeyValuePair(Of String, String) In AbskintoneModel.GetDefaultToPropertyDic
                If dictionary.ContainsKey(item.Key) Then 'デフォルト名称が使用されている場合
                    Dim defaultProp As PropertyInfo = objType.GetProperty(item.Value)
                    If defaultProp IsNot Nothing Then
                        Dim value As Object = readKintoneItem(defaultProp.PropertyType, dictionary(item.Key)("value"), serializer)
                        defaultProp.SetValue(m, value, Nothing)
                    End If
                End If
            Next

            'その他プロパティ処理
            Dim props As PropertyInfo() = objType.GetProperties()
            For Each p As PropertyInfo In props
                If dictionary.ContainsKey(p.Name) Then
                    Dim value As Object = readKintoneItem(p.PropertyType, dictionary(p.Name)("value"), serializer)
                    p.SetValue(m, value, Nothing)
                End If
            Next

            Return m

        End Function

        ''' <summary>
        ''' モデルのプロパティ型に合わせてデータのセットを行う
        ''' </summary>
        ''' <param name="pType"></param>
        ''' <param name="obj"></param>
        ''' <returns></returns>
        ''' <remarks></remarks>
        Private Function readKintoneItem(ByVal pType As Type, ByVal obj As Object, ByVal serializer As JavaScriptSerializer) As Object
            Dim result As Object = obj
            Dim t As Type = getGenericsType(pType)

            If t.BaseType = GetType(kintoneSubTableItem) Then '内部テーブル項目
                Dim list As ArrayList = CType(result, ArrayList)
                Dim genericsType As Type = GetType(List(Of )).MakeGenericType(t)
                Dim objArray = Activator.CreateInstance(genericsType)
                For Each item As Object In list
                    Dim innerTableRow = Deserialize(item("value"), t, serializer)
                    innerTableRow.id = item("id") '親クラスが kintoneSubTableItem であることは保証されているため、idプロパティは必ず存在する
                    objArray.Add(innerTableRow)
                Next
                result = objArray
            Else
                Select Case t
                    Case GetType(DateTime)
                        Dim d As DateTime = Nothing
                        result = kintoneDatetime.kintoneToDatetime(obj)
                    Case GetType(kintoneUser)
                        result = New kintoneUser(obj)
                    Case GetType(Decimal), GetType(Double), GetType(Integer)
                        If result Is Nothing OrElse String.IsNullOrWhiteSpace(result.ToString) Then
                            result = 0 '初期値を設定
                        End If
                End Select

                result = _serializer.ConvertToType(result, pType) '指定タイプでDeserialize
            End If

            Return result

        End Function

        Private Function getGenericsType(ByVal pType As Type) As Type
            If pType.IsGenericType Then
                Return pType.GetGenericArguments(0) '複数のジェネリクスは考慮しない
            Else
                Return pType
            End If
        End Function

    End Class

End Namespace
